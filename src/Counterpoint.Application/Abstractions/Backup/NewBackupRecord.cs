using System;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>One completed backup, ready to be recorded (docs/01_DATA_MODEL.md §8, <c>backup_record</c>).</summary>
/// <param name="Filename">The backup file's name, not its full path.</param>
/// <param name="TakenAt">When the snapshot was taken.</param>
/// <param name="SizeBytes">Plain byte size of the encrypted file on disk.</param>
/// <param name="Checksum">SHA-256 of the ciphertext, hex-encoded.</param>
/// <param name="SchemaVer">The schema version the database was at when snapshotted.</param>
/// <param name="LocalPath">Full path of the local copy.</param>
/// <param name="UsbStatus">
/// <c>NA</c>, <c>OK</c> or <c>FAILED</c> - <c>NA</c> until <c>P1-T15</c> copies to USB (FR-11.3).
/// </param>
/// <param name="CloudStatus">
/// <c>PENDING</c>, <c>OK</c>, <c>FAILED</c> or <c>SKIPPED</c> - <c>SKIPPED</c> until Phase 4 builds
/// the uploader (FR-11.5, CLAUDE.md "Not cloud-dependent").
/// </param>
/// <param name="LastError">
/// Why <paramref name="UsbStatus"/> is <c>FAILED</c> - the USB path was not present, or the copy
/// failed partway - or null when there is nothing to say (P1-T15, FR-11.3). Reuses
/// <c>backup_record.last_error</c>, the same generic "what went wrong" column Phase 4's cloud
/// retry uses; nothing here writes <c>attempts</c> or <c>cloud_key</c>, which stay at the schema's
/// defaults until then.
/// </param>
public sealed record NewBackupRecord(
    string Filename,
    DateTimeOffset TakenAt,
    long SizeBytes,
    string Checksum,
    string SchemaVer,
    string LocalPath,
    string UsbStatus,
    string CloudStatus,
    string? LastError = null);
