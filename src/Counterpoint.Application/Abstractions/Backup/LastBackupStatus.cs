using System;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>The most recent row of <c>backup_record</c>, for the dashboard and status bar (SRS FR-9.7, UI-09).</summary>
/// <param name="TakenAt">When the backup was taken.</param>
/// <param name="UsbStatus"><c>NA</c>, <c>OK</c> or <c>FAILED</c> (docs/01_DATA_MODEL.md §8).</param>
/// <param name="CloudStatus"><c>PENDING</c>, <c>OK</c>, <c>FAILED</c> or <c>SKIPPED</c>. Cloud upload is Phase 4.</param>
/// <param name="VerifiedAt">When the backup's integrity was last verified, or null if it never has been.</param>
/// <param name="LastError">Why <paramref name="UsbStatus"/> is <c>FAILED</c>, or null (P1-T15).</param>
public sealed record LastBackupStatus(
    DateTimeOffset TakenAt,
    string UsbStatus,
    string CloudStatus,
    DateTimeOffset? VerifiedAt,
    string? LastError = null);
