using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Backup.Crypto;
using Counterpoint.Backup.Format;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZstdSharp;

namespace Counterpoint.Backup.Snapshots;

/// <summary>
/// Takes one encrypted, checksummed backup of the till's database (SRS FR-11.1-11.4).
/// </summary>
/// <remarks>
/// <para>
/// Pipeline: <c>VACUUM INTO</c> a temporary file through <see cref="IDatabaseSnapshotSource"/>
/// (still SQLCipher-encrypted with the till's own key), zstd-compress the result, AES-256-GCM
/// encrypt it with a key derived from the owner's backup passphrase via Argon2id, write the header
/// and ciphertext to the backup folder under a temporary name, rename it into place, SHA-256 the
/// ciphertext, then record the row in <c>backup_record</c>.
/// </para>
/// <para>
/// <b>Ordering matters.</b> The encrypted file is written completely and renamed into place
/// <em>before</em> <c>backup_record</c> is touched. A crash before that point leaves nothing to
/// record. A crash after it leaves a finished file with no row - discoverable later by re-scanning
/// the folder, which is a tolerable failure. The reverse order (record first) could leave a row
/// pointing at a file that was never finished, and nothing afterwards could tell that apart from a
/// real, restorable backup.
/// </para>
/// <para>
/// No printer, no network, nothing that blocks the sale (CLAUDE.md invariant 7): this runs off a
/// schedule or on demand (<c>P1-T15</c> wires the schedule), never inside
/// <see cref="Counterpoint.Application.Abstractions.Persistence.IUnitOfWork"/>.
/// </para>
/// </remarks>
public sealed partial class SnapshotService
{
    private readonly IDatabaseSnapshotSource _snapshotSource;
    private readonly IBackupPassphraseStore _passphraseStore;
    private readonly IBackupRecordStore _recordStore;
    private readonly SnapshotOptions _options;
    private readonly Argon2Parameters _argon2Parameters;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SnapshotService> _logger;

    public SnapshotService(
        IDatabaseSnapshotSource snapshotSource,
        IBackupPassphraseStore passphraseStore,
        IBackupRecordStore recordStore,
        SnapshotOptions options,
        Argon2Parameters argon2Parameters,
        TimeProvider timeProvider,
        ILogger<SnapshotService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotSource);
        ArgumentNullException.ThrowIfNull(passphraseStore);
        ArgumentNullException.ThrowIfNull(recordStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(argon2Parameters);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _snapshotSource = snapshotSource;
        _passphraseStore = passphraseStore;
        _recordStore = recordStore;
        _options = options;
        _argon2Parameters = argon2Parameters;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<SnapshotService>.Instance;
    }

    /// <param name="usbDirectory">
    /// The USB folder to copy the finished, encrypted file into, or null/blank for none
    /// (SRS FR-11.3, P1-T15). Read fresh on every call rather than fixed at start-up in
    /// <see cref="SnapshotOptions"/>, because whether a USB drive is plugged in - and where the
    /// shop points at - can change between one backup and the next. Its absence, or the copy
    /// failing, is a warning: <see cref="SnapshotResult.UsbWarning"/> says why, but the local
    /// backup above has already succeeded and is recorded regardless (CLAUDE.md invariant 7).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">No backup passphrase has been set yet.</exception>
    public async Task<SnapshotResult> CreateSnapshotAsync(
        string? usbDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var passphrase = _passphraseStore.TryGetPassphrase();
        if (string.IsNullOrEmpty(passphrase))
        {
            throw new InvalidOperationException(
                "No backup passphrase has been set. Set one from the owner settings screen before " +
                "the first backup can be taken (SRS FR-11.4).");
        }

        Directory.CreateDirectory(_options.SnapshotDirectory);

        var takenAt = _timeProvider.GetUtcNow();
        var rawTempPath = Path.Combine(_options.SnapshotDirectory, "raw-" + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            var snapshot = await _snapshotSource.CreateRawCopyAsync(rawTempPath, cancellationToken)
                .ConfigureAwait(false);

            var rawBytes = await File.ReadAllBytesAsync(rawTempPath, cancellationToken).ConfigureAwait(false);

            byte[] compressed;
            using (var compressor = new Compressor())
            {
                compressed = compressor.Wrap(rawBytes).ToArray();
            }

            var salt = RandomNumberGenerator.GetBytes(BackupFileFormat.SaltSizeBytes);
            var nonce = RandomNumberGenerator.GetBytes(BackupFileFormat.NonceSizeBytes);
            var key = PassphraseKeyDerivation.DeriveKey(passphrase, salt, _argon2Parameters);

            var header = new BackupFileHeader(salt, nonce, _argon2Parameters, snapshot.SchemaVersion, takenAt);

            var ciphertext = new byte[compressed.Length];
            var tag = new byte[BackupFileFormat.TagSizeBytes];

            try
            {
                using var aesGcm = new AesGcm(key, BackupFileFormat.TagSizeBytes);
                aesGcm.Encrypt(nonce, compressed, ciphertext, tag, header.BuildAad());
            }
            finally
            {
                Array.Clear(key, 0, key.Length);
            }

            var checksum = SHA256.HashData(ciphertext);

            var filename = "counterpoint-" +
                takenAt.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".cpbk";
            var finalPath = Path.Combine(_options.SnapshotDirectory, filename);
            var writingPath = finalPath + ".writing";

            await using (var fileStream = new FileStream(writingPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await header.WriteHeaderAsync(fileStream, tag, checksum, cancellationToken).ConfigureAwait(false);
                await fileStream.WriteAsync(ciphertext, cancellationToken).ConfigureAwait(false);
                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Atomic on the same volume: whatever the rest of the system can see under this name
            // is either the previous complete file or the new complete one, never a half-written
            // one - see the class remarks on ordering.
            File.Move(writingPath, finalPath, overwrite: true);

            var sizeBytes = new FileInfo(finalPath).Length;
            var checksumHex = Convert.ToHexString(checksum);

            var (usbStatus, usbWarning) = TryCopyToUsb(finalPath, filename, usbDirectory);

            // Recorded only now that the file is safely on disk under its final name.
            await _recordStore.RecordAsync(
                new NewBackupRecord(
                    filename,
                    takenAt,
                    sizeBytes,
                    checksumHex,
                    snapshot.SchemaVersion,
                    finalPath,
                    UsbStatus: usbStatus,
                    CloudStatus: "SKIPPED",
                    LastError: usbWarning),
                cancellationToken).ConfigureAwait(false);

            return new SnapshotResult(
                finalPath, filename, sizeBytes, checksumHex, snapshot.SchemaVersion, takenAt, usbStatus, usbWarning);
        }
        finally
        {
            if (File.Exists(rawTempPath))
            {
                File.Delete(rawTempPath);
            }
        }
    }

    /// <summary>
    /// Copies the already-finished, already-checksummed local backup file onto
    /// <paramref name="usbDirectory"/>, if one is configured (SRS FR-11.3). Never throws: a
    /// missing drive or a failed copy is a warning the local backup - already safely on disk by
    /// the time this runs - must not be undone by (CLAUDE.md invariant 7, P1-T15 Done-when
    /// "pointing the USB path at a missing location produces a warning and the local backup still
    /// succeeds").
    /// </summary>
    private (string Status, string? Warning) TryCopyToUsb(string finishedLocalPath, string filename, string? usbDirectory)
    {
        if (string.IsNullOrWhiteSpace(usbDirectory))
        {
            return ("NA", null);
        }

        if (!Directory.Exists(usbDirectory))
        {
            UsbPathMissing(_logger, usbDirectory);
            return ("FAILED", "The USB folder " + usbDirectory + " could not be found. Plug the drive in and check the path in Settings.");
        }

        var destination = Path.Combine(usbDirectory, filename);
        var writingDestination = destination + ".writing";

        try
        {
            File.Copy(finishedLocalPath, writingDestination, overwrite: true);
            File.Move(writingDestination, destination, overwrite: true);
            return ("OK", null);
        }
#pragma warning disable CA1031 // A USB copy failure is a warning for every failure mode a
        // removable drive can produce, not a specific one - CLAUDE.md
        // invariant 7: it must never fail the local backup that already
        // succeeded.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            UsbCopyFailed(_logger, usbDirectory, exception);

            try
            {
                if (File.Exists(writingDestination))
                {
                    File.Delete(writingDestination);
                }
            }
            catch (IOException)
            {
                // Best effort only - a stray .writing file on the USB drive is cleaned up by the
                // next backup's overwrite, and is not itself a reason to escalate this failure.
            }
            catch (UnauthorizedAccessException)
            {
            }

            return ("FAILED", "The USB copy failed: " + exception.Message);
        }
    }

    [LoggerMessage(
        EventId = 7401,
        Level = LogLevel.Warning,
        Message = "USB backup folder {UsbDirectory} was not found. The local backup still succeeded; "
            + "plug the drive in and check the path in Settings.")]
    private static partial void UsbPathMissing(ILogger logger, string usbDirectory);

    [LoggerMessage(
        EventId = 7402,
        Level = LogLevel.Warning,
        Message = "Copying the backup to USB folder {UsbDirectory} failed. The local backup still succeeded.")]
    private static partial void UsbCopyFailed(ILogger logger, string usbDirectory, Exception exception);
}
