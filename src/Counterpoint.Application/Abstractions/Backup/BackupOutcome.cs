using System;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>What one call to <see cref="IBackupOrchestrator.RunAsync"/> produced (P1-T15).</summary>
/// <param name="Trigger">What asked for this backup.</param>
/// <param name="Succeeded">
/// Whether the local backup was taken and recorded. A USB problem never makes this false
/// (CLAUDE.md invariant 7, FR-11.3) - see <see cref="UsbWarning"/> for that.
/// </param>
/// <param name="Filename">The backup file's name, when <paramref name="Succeeded"/> is true.</param>
/// <param name="TakenAt">When the snapshot was taken, when <paramref name="Succeeded"/> is true.</param>
/// <param name="UsbStatus"><c>NA</c>, <c>OK</c> or <c>FAILED</c> - mirrors <c>backup_record.usb_status</c>.</param>
/// <param name="UsbWarning">Why the USB copy did not happen, in plain language, or null.</param>
/// <param name="FailureReason">
/// Why the local backup itself failed, in plain language, when <paramref name="Succeeded"/> is
/// false - for example, no backup passphrase has been set yet.
/// </param>
public sealed record BackupOutcome(
    BackupTrigger Trigger,
    bool Succeeded,
    string? Filename,
    DateTimeOffset? TakenAt,
    string UsbStatus,
    string? UsbWarning,
    string? FailureReason)
{
    public static BackupOutcome Failed(BackupTrigger trigger, string reason) =>
        new(trigger, Succeeded: false, null, null, UsbStatus: "NA", null, reason);
}
