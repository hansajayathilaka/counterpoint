using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// Triggers a backup after a shift close, if <c>backup.on_shift_close</c> is enabled (SRS FR-11.1,
/// task P3-T03 "Do this" #2). The Application-layer seam <see cref="Counterpoint.Application.Shifts.ICloseShift"/>
/// calls, because <c>Counterpoint.Application</c> may not reference <c>Counterpoint.Backup</c>
/// directly (CLAUDE.md "Project boundaries") - the composition root wires the real implementation
/// the same way <see cref="IBackupOrchestrator"/> and <see cref="IManualBackupTrigger"/> are wired.
/// </summary>
/// <remarks>
/// Called only after the close transaction has already committed (CLAUDE.md invariant 7: no
/// network- or file-touching work inside a database transaction). Never throws - a failed backup
/// degrades to a reported warning, exactly as every other peripheral failure does; the shift is
/// already closed and durable by the time this runs, and nothing here can undo that.
/// </remarks>
public interface IShiftCloseBackupTrigger
{
    /// <summary>
    /// Runs the shift-close backup when the setting asks for one. Returns null when the setting is
    /// off, so nothing ran.
    /// </summary>
    public Task<BackupOutcome?> RunIfEnabledAsync(CancellationToken cancellationToken = default);
}
