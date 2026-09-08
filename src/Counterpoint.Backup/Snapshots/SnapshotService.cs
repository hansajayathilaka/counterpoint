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
public sealed class SnapshotService
{
    private readonly IDatabaseSnapshotSource _snapshotSource;
    private readonly IBackupPassphraseStore _passphraseStore;
    private readonly IBackupRecordStore _recordStore;
    private readonly SnapshotOptions _options;
    private readonly Argon2Parameters _argon2Parameters;
    private readonly TimeProvider _timeProvider;

    public SnapshotService(
        IDatabaseSnapshotSource snapshotSource,
        IBackupPassphraseStore passphraseStore,
        IBackupRecordStore recordStore,
        SnapshotOptions options,
        Argon2Parameters argon2Parameters,
        TimeProvider timeProvider)
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
    }

    /// <exception cref="InvalidOperationException">No backup passphrase has been set yet.</exception>
    public async Task<SnapshotResult> CreateSnapshotAsync(CancellationToken cancellationToken = default)
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

            // Recorded only now that the file is safely on disk under its final name.
            await _recordStore.RecordAsync(
                new NewBackupRecord(
                    filename,
                    takenAt,
                    sizeBytes,
                    checksumHex,
                    snapshot.SchemaVersion,
                    finalPath,
                    UsbStatus: "NA",
                    CloudStatus: "SKIPPED"),
                cancellationToken).ConfigureAwait(false);

            return new SnapshotResult(finalPath, filename, sizeBytes, checksumHex, snapshot.SchemaVersion, takenAt);
        }
        finally
        {
            if (File.Exists(rawTempPath))
            {
                File.Delete(rawTempPath);
            }
        }
    }
}
