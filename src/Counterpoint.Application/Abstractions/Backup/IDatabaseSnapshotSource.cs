using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// Produces and verifies a raw copy of the till's encrypted database file (SRS FR-11.1-11.4).
/// </summary>
/// <remarks>
/// <para>
/// A port, not a service. <c>Counterpoint.Backup</c> knows how to compress, encrypt and checksum
/// a backup, but it may not reference <c>Counterpoint.Infrastructure</c> (CLAUDE.md
/// "Project boundaries"), so it cannot open a SQLCipher connection itself. This is the one seam
/// that boundary crosses: <c>Counterpoint.Infrastructure</c> implements it against the live write
/// connection and the database key store, and the composition root hands the interface to
/// <c>Counterpoint.Backup</c>'s <c>SnapshotService</c> and <c>RestoreService</c>.
/// </para>
/// <para>
/// <c>VACUUM INTO</c>, not a file copy, for <see cref="CreateRawCopyAsync"/> - consistent without
/// stopping writers, and SQLCipher re-encrypts the output with the same key, exactly as
/// <c>MigrationRunner</c>'s pre-migration copy does. The output is still SQLCipher-encrypted with
/// the till's own database key; <c>Counterpoint.Backup</c> compresses and separately encrypts it
/// with the owner's backup passphrase on top of that.
/// </para>
/// </remarks>
public interface IDatabaseSnapshotSource
{
    /// <summary>
    /// Writes a consistent, SQLCipher-encrypted copy of the live database to
    /// <paramref name="destinationFilePath"/> and reports the schema version it was taken at.
    /// </summary>
    public Task<DatabaseSnapshotResult> CreateRawCopyAsync(
        string destinationFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens <paramref name="sqliteFilePath"/> with the till's database key, runs
    /// <c>PRAGMA integrity_check</c> and counts every row in every user table. Used by
    /// <c>RestoreService</c> to prove a restored file is a real, undamaged database rather than
    /// merely a file that decrypted without error.
    /// </summary>
    public Task<DatabaseVerificationResult> VerifyRawCopyAsync(
        string sqliteFilePath,
        CancellationToken cancellationToken = default);
}
