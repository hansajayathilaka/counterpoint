using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// Drains the <c>print_job</c> outbox onto the printer (SAD §8, SRS FR-7.8, AC-16).
/// </summary>
/// <remarks>
/// <para>
/// This is the far side of "never block the sale" (CLAUDE.md invariant 7). The bill was made
/// durable, and the drawer opened, before anything here ran. Nothing this class does can affect
/// a bill, and nothing it fails at may stop it running for the next one.
/// </para>
/// <para>
/// Two rules it keeps mechanically:
/// </para>
/// <list type="bullet">
///   <item><b>No transaction is held across a printer call.</b> Each outbox operation is a unit
///   of work of its own: read a job, close; print; record the result, close. A printer that
///   takes ten seconds to answer must not hold the till's single writer for ten seconds.</item>
///   <item><b>Nothing escapes <see cref="ExecuteAsync"/>.</b> An unhandled exception from a
///   <c>BackgroundService</c> stops the host, which on this design would take the sales screen
///   down because a receipt did not print.</item>
/// </list>
/// </remarks>
public sealed partial class PrintWorker : BackgroundService
{
    private readonly IPrintJobOutbox _outbox;
    private readonly IReceiptPrinter _printer;
    private readonly PrintWorkerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PrintWorker> _logger;

    public PrintWorker(
        IPrintJobOutbox outbox,
        IReceiptPrinter printer,
        PrintWorkerOptions options,
        TimeProvider timeProvider,
        ILogger<PrintWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(printer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _outbox = outbox;
        _printer = printer;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Prints everything that is pending, stopping at the first job that fails with retries
    /// left - that job goes to the front of the next pass rather than being skipped, because a
    /// receipt printed out of order is worse than one printed late.
    /// </summary>
    /// <returns>How many jobs were printed successfully.</returns>
    /// <remarks>
    /// Public so a test can drive one pass without waiting for a poll interval. The service
    /// loop calls exactly this.
    /// </remarks>
    public async Task<int> DrainAsync(CancellationToken cancellationToken = default)
    {
        var printed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var job = await _outbox.NextPendingAsync(cancellationToken).ConfigureAwait(false);
            if (job is null)
            {
                break;
            }

            // Outside every transaction: the read above has already committed and closed.
            var outcome = await _printer
                .PrintAsync(job.Payload, JobNameFor(job), cancellationToken)
                .ConfigureAwait(false);

            if (outcome.Succeeded)
            {
                await _outbox.MarkPrintedAsync(job.Id, cancellationToken).ConfigureAwait(false);
                printed++;
                continue;
            }

            var giveUp = job.Attempts + 1 >= _options.MaxAttempts;

            await _outbox.RecordFailedAttemptAsync(
                job.Id,
                outcome.FailureReason ?? "The printer did not accept the document.",
                giveUp,
                cancellationToken).ConfigureAwait(false);

            if (giveUp)
            {
                JobGivenUp(job.Id, job.Attempts + 1, outcome.FailureReason);

                // It is FAILED now, so the next NextPendingAsync returns a different job.
                continue;
            }

            JobDeferred(job.Id, job.Attempts + 1, outcome.FailureReason);

            // Still PENDING. Stop the pass rather than spin on it; the poll interval is the wait.
            break;
        }

        return printed;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // Catch-all is the requirement here, not an oversight.
            catch (Exception exception)
            {
                // Deliberately everything. This loop must outlive any fault the outbox or the
                // printer can produce: a worker that stopped would silently queue receipts for
                // the rest of the trading day, and an escaping exception would stop the host.
                DrainFailed(exception);
            }
#pragma warning restore CA1031

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

    /// <summary>A name a human can match to a document in the spooler or the artefact folder.</summary>
    private static string JobNameFor(PendingPrintJob job) => string.Create(
        CultureInfo.InvariantCulture,
        $"{job.DocType}-{job.DocId?.ToString(CultureInfo.InvariantCulture) ?? job.Id.ToString(CultureInfo.InvariantCulture)}");

    [LoggerMessage(
        EventId = 7101,
        Level = LogLevel.Warning,
        Message = "Print job {PrintJobId} failed on attempt {Attempt} and will be retried: {Reason}")]
    private partial void JobDeferred(long printJobId, int attempt, string? reason);

    [LoggerMessage(
        EventId = 7102,
        Level = LogLevel.Warning,
        Message = "Print job {PrintJobId} was given up after {Attempt} attempts: {Reason}. "
            + "The bill is unaffected; reprint it from the queue when the printer is back.")]
    private partial void JobGivenUp(long printJobId, int attempt, string? reason);

    [LoggerMessage(
        EventId = 7103,
        Level = LogLevel.Error,
        Message = "The print outbox could not be drained. Trading is unaffected; the worker will try again.")]
    private partial void DrainFailed(Exception exception);
}
