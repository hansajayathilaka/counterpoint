using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Backup.Orchestration;

/// <summary>
/// Takes the daily backup unattended, at the configured local time (SRS FR-11.1, P1-T15).
/// </summary>
/// <remarks>
/// <para>
/// <b>Idempotent by construction, not by a saved "last ran" flag.</b> Every tick asks
/// <see cref="ILastBackupStatusReader"/> whether a backup has already been taken today - manually,
/// on a shift close, or by an earlier tick - before running one. A backup taken any way at all
/// today satisfies FR-11.1's "at least once per day"; there is nothing to gain from a second one,
/// and nothing lost by checking the database instead of remembering the answer in memory, which a
/// process restart would forget anyway.
/// </para>
/// <para>
/// <b>Catch-up, not "missed it, oh well".</b> If the till is off at the configured time and starts
/// later the same day, the first tick after start-up is already past the daily time and no backup
/// exists yet for today, so it runs immediately rather than waiting for tomorrow.
/// </para>
/// <para>
/// <b>Never blocks the sale (CLAUDE.md invariant 7).</b> This is its own <see cref="BackgroundService"/>
/// loop, entirely off the UI thread and outside every business transaction; the snapshot,
/// compress, encrypt and USB-copy steps it drives are the same ones <c>SnapshotService</c>'s own
/// remarks already establish do not take the single write connection's lock.
/// </para>
/// </remarks>
public sealed partial class BackupScheduler : BackgroundService
{
    private readonly IBackupOrchestrator _orchestrator;
    private readonly ISettings _settings;
    private readonly ILastBackupStatusReader _lastBackup;
    private readonly BackupSchedulerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BackupScheduler> _logger;

    public BackupScheduler(
        IBackupOrchestrator orchestrator,
        ISettings settings,
        ILastBackupStatusReader lastBackup,
        BackupSchedulerOptions options,
        TimeProvider timeProvider,
        ILogger<BackupScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(lastBackup);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _orchestrator = orchestrator;
        _settings = settings;
        _lastBackup = lastBackup;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs a shift-close backup when <c>backup.on_shift_close</c> is enabled (SRS FR-11.1).
    /// </summary>
    /// <remarks>
    /// Nothing calls this yet: shift close is <c>P3-T01</c>/<c>P3-T03</c>, not built in this task's
    /// dependency set. It is ready for that command to call the moment it exists - honest about
    /// what is and is not wired, the same way <c>SalesViewModel.StatusBackupText</c> was before
    /// this task gave it a real answer.
    /// </remarks>
    public async Task RunAfterShiftCloseAsync(CancellationToken cancellationToken = default)
    {
        if (!_settings.Current.Backup.BackupOnShiftClose)
        {
            return;
        }

        await _orchestrator.RunAsync(BackupTrigger.ShiftClose, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes the daily backup if it is due and has not already happened today. Public so a test
    /// can drive one check without waiting for <see cref="BackupSchedulerOptions.PollInterval"/>,
    /// the same shape as <c>Counterpoint.Devices.Printing.PrintWorker.DrainAsync</c>.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetLocalNow();
        var dailyTime = _settings.Current.Backup.DailyBackupTime;

        if (now.TimeOfDay < dailyTime.ToTimeSpan())
        {
            return;
        }

        var last = await _lastBackup.GetLastAsync(cancellationToken).ConfigureAwait(false);
        if (last is not null && WasTakenToday(last.TakenAt))
        {
            return;
        }

        // Falls through to running one otherwise - including catch-up, when the till started
        // later the same day, already past the daily time, with nothing recorded for today yet.

        await _orchestrator.RunAsync(BackupTrigger.Scheduled, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Catch-all is the requirement here: this loop must outlive any
            // fault the settings store, the record reader or the orchestrator
            // can produce (CLAUDE.md invariant 7).
            catch (Exception exception)
#pragma warning restore CA1031
            {
                TickFailed(_logger, exception);
            }

            try
            {
                await Task.Delay(_options.PollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="takenAt"/> falls on the shop's current local calendar day, by this
    /// scheduler's own <c>TimeProvider</c>'s notion of local time - never
    /// <see cref="TimeZoneInfo.Local"/> directly, which a test holding the clock still would not
    /// be reading from.
    /// </summary>
    private bool WasTakenToday(DateTimeOffset takenAt) =>
        TimeZoneInfo.ConvertTime(takenAt, _timeProvider.LocalTimeZone).Date
            == TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), _timeProvider.LocalTimeZone).Date;

    [LoggerMessage(
        EventId = 7503,
        Level = LogLevel.Error,
        Message = "The backup scheduler's check could not run. Trading is unaffected; it will try again.")]
    private static partial void TickFailed(ILogger logger, Exception exception);
}
