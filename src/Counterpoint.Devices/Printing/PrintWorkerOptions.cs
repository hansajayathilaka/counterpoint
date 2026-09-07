using System;

namespace Counterpoint.Devices.Printing;

/// <summary>
/// How <see cref="PrintWorker"/> drains the outbox.
/// </summary>
public sealed record PrintWorkerOptions
{
    /// <summary>
    /// How long to wait before looking again once the queue is empty or a job has failed.
    /// </summary>
    /// <remarks>
    /// Injectable so a test does not have to sleep for seconds to prove a retry happened. Half a
    /// second at the till: a receipt that appears half a second after the drawer opens is
    /// indistinguishable from an instant one, and the partial index on <c>print_job.status</c>
    /// keeps an empty poll close to free.
    /// </remarks>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How many times a job is tried before it is given up on and marked <c>FAILED</c>
    /// (SAD §8: retry ×3). A failed job stays in the outbox for the reprint queue; it is never
    /// deleted, and the bill it belongs to was never in doubt.
    /// </summary>
    public int MaxAttempts { get; init; } = 3;
}
