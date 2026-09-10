using System;

namespace Counterpoint.Backup.Snapshots;

/// <summary>What one call to <see cref="SnapshotService.CreateSnapshotAsync"/> produced.</summary>
/// <param name="FilePath">Full path of the encrypted backup file.</param>
/// <param name="Filename">Just the file's name, as recorded in <c>backup_record.filename</c>.</param>
/// <param name="SizeBytes">Plain byte size of the encrypted file on disk.</param>
/// <param name="Checksum">SHA-256 of the ciphertext, hex-encoded.</param>
/// <param name="SchemaVersion">The schema version the database was at when snapshotted.</param>
/// <param name="TakenAt">When the snapshot was taken.</param>
/// <param name="UsbStatus">
/// <c>NA</c> (no USB path configured), <c>OK</c> (copied) or <c>FAILED</c> (a path was configured
/// but the copy did not happen - the drive is missing, or the copy itself failed). Mirrors
/// <c>backup_record.usb_status</c> exactly, because this is what was written there (P1-T15, FR-11.3).
/// </param>
/// <param name="UsbWarning">
/// Why <see cref="UsbStatus"/> is <c>FAILED</c>, in plain language, or null. A missing or failed
/// USB copy is a warning, never an exception - the local backup above already succeeded by the
/// time this is known.
/// </param>
public sealed record SnapshotResult(
    string FilePath,
    string Filename,
    long SizeBytes,
    string Checksum,
    string SchemaVersion,
    DateTimeOffset TakenAt,
    string UsbStatus = "NA",
    string? UsbWarning = null);
