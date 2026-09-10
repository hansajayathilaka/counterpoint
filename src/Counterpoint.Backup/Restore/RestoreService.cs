using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Backup.Crypto;
using Counterpoint.Backup.Format;
using ZstdSharp;

namespace Counterpoint.Backup.Restore;

/// <summary>
/// Reverses <see cref="Counterpoint.Backup.Snapshots.SnapshotService"/>: verify the checksum,
/// decrypt, decompress, write the raw database file out, then prove it is real by running
/// <c>PRAGMA integrity_check</c> and counting its rows (SRS FR-11.4).
/// </summary>
/// <remarks>
/// <para>
/// The passphrase is a parameter, not read from <see cref="Application.Abstractions.Security.IBackupPassphraseStore"/>:
/// a restore is exactly the operation a shop might run onto a fresh install with no protected
/// store populated yet, so the operator types the passphrase they were given when it was set.
/// </para>
/// <para>
/// Checksum before decrypt, not the other way around: a mismatch there is cheap to detect and
/// means the file is damaged regardless of whether the passphrase is even right, so there is no
/// reason to spend an Argon2id derivation - the expensive step - on a file that already fails the
/// simple check.
/// </para>
/// <para>Local only (Phase 0). Restoring from the cloud copy is Phase 4's problem; this service
/// only ever reads a file path it is given.</para>
/// </remarks>
public sealed class RestoreService
{
    private readonly IDatabaseSnapshotSource _snapshotSource;

    public RestoreService(IDatabaseSnapshotSource snapshotSource)
    {
        ArgumentNullException.ThrowIfNull(snapshotSource);
        _snapshotSource = snapshotSource;
    }

    /// <exception cref="BackupRestoreException">
    /// The file is not a Counterpoint backup, is truncated, fails its checksum, the passphrase is
    /// wrong, or the restored database fails its integrity check - each with a plain-language
    /// message (SRS FR-11.4).
    /// </exception>
    public async Task<RestoreResult> RestoreAsync(
        string backupFilePath,
        string passphrase,
        string destinationFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        var (header, tag, aad, ciphertext) = await ReadAndVerifyChecksumAsync(backupFilePath, cancellationToken)
            .ConfigureAwait(false);

        var key = PassphraseKeyDerivation.DeriveKey(passphrase, header.Salt, header.Argon2);
        byte[] compressed;
        try
        {
            compressed = new byte[ciphertext.Length];
            using var aesGcm = new AesGcm(key, BackupFileFormat.TagSizeBytes);
            aesGcm.Decrypt(header.Nonce, ciphertext, tag, compressed, aad);
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new BackupRestoreException(
                "The passphrase is incorrect, so this backup cannot be decrypted.", ex);
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
        }

        byte[] rawBytes;
        using (var decompressor = new Decompressor())
        {
            var decompressedSize = Decompressor.GetDecompressedSize(compressed);
            rawBytes = decompressor.Unwrap(compressed, checked((int)decompressedSize)).ToArray();
        }

        var directory = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllBytesAsync(destinationFilePath, rawBytes, cancellationToken).ConfigureAwait(false);

        var verification = await _snapshotSource.VerifyRawCopyAsync(destinationFilePath, cancellationToken)
            .ConfigureAwait(false);

        if (!verification.IntegrityOk)
        {
            throw new BackupRestoreException(
                "The restored database failed its integrity check: " + verification.IntegrityMessage);
        }

        return new RestoreResult(
            destinationFilePath,
            header.SchemaVersion,
            header.TakenAt,
            verification.RowCountsByTable,
            verification.TotalRowCount);
    }

    /// <summary>
    /// Reads the header and verifies the checksum of <paramref name="backupFilePath"/>, without
    /// touching the passphrase or decrypting anything - what the guided restore wizard needs to
    /// show the data date before it asks for one (SRS FR-11.12: "verify checksum" and "show what
    /// date the data will be restored to" come before "prompt for the passphrase").
    /// </summary>
    /// <exception cref="BackupRestoreException">
    /// The file is not a Counterpoint backup, is truncated, or its checksum does not match its
    /// contents.
    /// </exception>
    public static async Task<BackupPreview> PreviewAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);

        var (header, _, _, _) = await ReadAndVerifyChecksumAsync(backupFilePath, cancellationToken)
            .ConfigureAwait(false);

        return new BackupPreview(header.TakenAt, header.SchemaVersion);
    }

    /// <summary>
    /// Reads the header from <paramref name="backupFilePath"/> and verifies its checksum, shared
    /// by <see cref="RestoreAsync"/> and <see cref="PreviewAsync"/> so the two can never check the
    /// checksum two different ways.
    /// </summary>
    private static async Task<(BackupFileHeader Header, byte[] Tag, byte[] Aad, byte[] Ciphertext)> ReadAndVerifyChecksumAsync(
        string backupFilePath,
        CancellationToken cancellationToken)
    {
        BackupFileHeader header;
        byte[] tag;
        byte[] checksum;
        byte[] aad;
        byte[] ciphertext;

        await using (var source = new FileStream(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            (header, tag, checksum, aad) = await BackupFileHeader.ReadAsync(source, cancellationToken)
                .ConfigureAwait(false);

            ciphertext = new byte[source.Length - source.Position];
            var read = 0;
            while (read < ciphertext.Length)
            {
                var n = await source.ReadAsync(ciphertext.AsMemory(read), cancellationToken).ConfigureAwait(false);
                if (n == 0)
                {
                    throw new BackupRestoreException(
                        "This backup file is shorter than its own header says - it is truncated.");
                }

                read += n;
            }
        }

        var actualChecksum = SHA256.HashData(ciphertext);
        if (!CryptographicOperations.FixedTimeEquals(actualChecksum, checksum))
        {
            throw new BackupRestoreException(
                "This backup file is damaged: its stored checksum does not match its contents.");
        }

        return (header, tag, aad, ciphertext);
    }
}
