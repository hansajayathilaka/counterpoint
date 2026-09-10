using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Backup.Snapshots;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Counterpoint.Backup.Orchestration;

/// <summary>
/// Implements both <see cref="IBackupOrchestrator"/> (no role check - the schedule and a shift
/// close call this with nobody signed in) and <see cref="IManualBackupTrigger"/> (the owner's
/// "Backup now" button, reached only through the role-decorated interface) with one snapshot
/// pipeline (SRS FR-11.1-11.3, P1-T15).
/// </summary>
/// <remarks>
/// Internal, like every concrete service behind an interface carrying
/// <see cref="RequiresRoleAttribute"/> - the composition root builds one, decorates it for
/// <see cref="IManualBackupTrigger"/>, and separately hands out the undecorated
/// <see cref="IBackupOrchestrator"/> to <see cref="BackupScheduler"/>, the same shape
/// <c>FirstRunSetupService</c> reaches <c>BackupPassphraseStore</c>'s internal seam with.
/// </remarks>
internal sealed partial class BackupOrchestrator : IBackupOrchestrator, IManualBackupTrigger
{
    private readonly SnapshotService _snapshotService;
    private readonly ISettings _settings;
    private readonly ILogger<BackupOrchestrator> _logger;

    public BackupOrchestrator(SnapshotService snapshotService, ISettings settings, ILogger<BackupOrchestrator>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(snapshotService);
        ArgumentNullException.ThrowIfNull(settings);

        _snapshotService = snapshotService;
        _settings = settings;
        _logger = logger ?? NullLogger<BackupOrchestrator>.Instance;
    }

    /// <inheritdoc cref="IBackupOrchestrator.RunAsync" />
    public async Task<BackupOutcome> RunAsync(BackupTrigger trigger, CancellationToken cancellationToken = default)
    {
        var usbPath = _settings.Current.Backup.UsbPath;
        var usbDirectory = string.IsNullOrWhiteSpace(usbPath) ? null : usbPath;

        try
        {
            var snapshot = await _snapshotService.CreateSnapshotAsync(usbDirectory, cancellationToken)
                .ConfigureAwait(false);

            BackupCompleted(_logger, trigger, snapshot.Filename, snapshot.UsbStatus);

            return new BackupOutcome(
                trigger,
                Succeeded: true,
                snapshot.Filename,
                snapshot.TakenAt,
                snapshot.UsbStatus,
                snapshot.UsbWarning,
                FailureReason: null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            // Never blocks the sale, and never stops the scheduler's next attempt (CLAUDE.md
            // invariant 7): a failed backup is reported, not thrown past this boundary, because
            // both callers - the unattended scheduler and the owner's button - need a plain
            // outcome rather than an exception to show or log.
            BackupFailed(_logger, trigger, exception);
            return BackupOutcome.Failed(trigger, exception.Message);
        }
    }

    /// <inheritdoc />
    public Task<BackupOutcome> RunNowAsync(CancellationToken cancellationToken = default) =>
        RunAsync(BackupTrigger.Manual, cancellationToken);

    [LoggerMessage(
        EventId = 7501,
        Level = LogLevel.Information,
        Message = "Backup {Trigger} completed: {Filename} (USB: {UsbStatus}).")]
    private static partial void BackupCompleted(ILogger logger, BackupTrigger trigger, string filename, string usbStatus);

    [LoggerMessage(
        EventId = 7502,
        Level = LogLevel.Warning,
        Message = "Backup {Trigger} failed. Trading is unaffected; the next scheduled or manual attempt will try again.")]
    private static partial void BackupFailed(ILogger logger, BackupTrigger trigger, Exception exception);
}
