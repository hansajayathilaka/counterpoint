using System;

namespace Counterpoint.Backup.Snapshots;

/// <summary>What one call to <see cref="SnapshotService.CreateSnapshotAsync"/> produced.</summary>
/// <param name="FilePath">Full path of the encrypted backup file.</param>
/// <param name="Filename">Just the file's name, as recorded in <c>backup_record.filename</c>.</param>
/// <param name="SizeBytes">Plain byte size of the encrypted file on disk.</param>
/// <param name="Checksum">SHA-256 of the ciphertext, hex-encoded.</param>
/// <param name="SchemaVersion">The schema version the database was at when snapshotted.</param>
/// <param name="TakenAt">When the snapshot was taken.</param>
public sealed record SnapshotResult(
    string FilePath,
    string Filename,
    long SizeBytes,
    string Checksum,
    string SchemaVersion,
    DateTimeOffset TakenAt);
