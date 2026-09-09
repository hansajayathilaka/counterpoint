using System;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One <c>print_job</c> row as the print-queue screen shows it: pending or failed, never
/// printed - a job already printed has nothing left for a cashier to act on (P1-T11's print
/// queue UI).
/// </summary>
/// <param name="Id">The outbox row id - what <see cref="IPrintJobOutbox.RetryAsync"/> takes.</param>
/// <param name="DocType">A <c>print_job.doc_type</c> value, for example <c>SALE</c>.</param>
/// <param name="DocId">The document it belongs to, or null.</param>
/// <param name="Status"><c>PENDING</c> or <c>FAILED</c>.</param>
/// <param name="Attempts">How many times printing has been tried so far.</param>
/// <param name="LastError">The most recent failure reason, or null if it has never failed.</param>
/// <param name="CreatedAt">When the job was queued.</param>
public sealed record PrintQueueEntry(
    long Id,
    string DocType,
    long? DocId,
    string Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAt);
