using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The print outbox (SAD §8, SRS FR-7.8, AC-16).
/// </summary>
/// <remarks>
/// <para>
/// This is the whole of "never block the sale" (CLAUDE.md invariant 7). The sale transaction
/// ends at <see cref="EnqueueAsync"/> - a row, not a printer call. A background worker takes
/// the row afterwards, prints it outside any transaction, and records what happened. A printer
/// that is out of paper, unplugged or on fire changes nothing about the bill.
/// </para>
/// <para>
/// Each method is a unit of work of its own, so the worker never holds a transaction open
/// across a printer call.
/// </para>
/// </remarks>
public interface IPrintJobOutbox
{
    /// <summary>
    /// Queues a document, in the caller's transaction, and returns the outbox row id.
    /// </summary>
    public Task<long> EnqueueAsync(PrintJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the oldest pending job, or null when the queue is empty.
    /// </summary>
    public Task<PendingPrintJob?> NextPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Marks a job as printed.</summary>
    public Task MarkPrintedAsync(long printJobId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a failed attempt.
    /// </summary>
    /// <param name="printJobId">The job that failed.</param>
    /// <param name="failureReason">The plain-language reason, for the reprint queue and the log.</param>
    /// <param name="giveUp">
    /// True when the retry budget is spent: the job moves to <c>FAILED</c> and stops being
    /// picked up. False leaves it <c>PENDING</c> for the next poll.
    /// </param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public Task RecordFailedAttemptAsync(
        long printJobId,
        string failureReason,
        bool giveUp,
        CancellationToken cancellationToken = default);
}
