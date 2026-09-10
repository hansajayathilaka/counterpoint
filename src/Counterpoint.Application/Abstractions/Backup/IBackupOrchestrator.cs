using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// Takes one backup - local, then USB if configured - for whichever reason asked for it
/// (SRS FR-11.1-11.3, P1-T15).
/// </summary>
/// <remarks>
/// No <see cref="Counterpoint.Application.Security.RequiresRoleAttribute"/>: the daily schedule
/// and a shift close call this with nobody signed in, so authorisation cannot sit here. The
/// owner's "Backup now" button goes through <see cref="IManualBackupTrigger"/> instead, which
/// wraps the same implementation behind the owner-only check (SRS FR-11.2, NFR-S2, AC-17).
/// </remarks>
public interface IBackupOrchestrator
{
    /// <summary>
    /// Takes a snapshot, copies it to USB if <c>backup.usb_path</c> is configured and present, and
    /// records the outcome. Never throws for a USB problem - only for the local backup itself
    /// failing (for example, no passphrase set).
    /// </summary>
    public Task<BackupOutcome> RunAsync(BackupTrigger trigger, CancellationToken cancellationToken = default);
}
