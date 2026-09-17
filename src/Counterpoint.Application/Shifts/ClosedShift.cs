using Counterpoint.Application.Abstractions.Backup;

namespace Counterpoint.Application.Shifts;

/// <summary>The result of closing a shift (task P3-T03, SRS FR-8.4).</summary>
/// <param name="Report">The Z report's full figures, exactly as printed.</param>
/// <param name="PrintJobId">
/// The <c>print_job</c> outbox row queued for the Z report, written inside the same transaction as
/// the close itself (task P3-T03 "Do this" #2) - unlike the X report, which prints synchronously
/// with no outbox row, a shift close is a transaction to enqueue against.
/// </param>
/// <param name="BackupOutcome">
/// What <see cref="IShiftCloseBackupTrigger.RunIfEnabledAsync"/> reported, taken after the close
/// transaction committed (SRS FR-11.1). Null when <c>backup.on_shift_close</c> is off, so no
/// backup ran at all - never a reason the close itself failed either way.
/// </param>
public sealed record ClosedShift(ZReportSummary Report, long PrintJobId, BackupOutcome? BackupOutcome);
