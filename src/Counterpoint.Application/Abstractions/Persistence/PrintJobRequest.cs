namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// A document to be printed, queued in the outbox inside the business transaction that produced
/// it (CLAUDE.md invariant 7, SAD §8).
/// </summary>
/// <param name="DocType">A <c>print_job.doc_type</c> value, for example <c>SALE</c>.</param>
/// <param name="DocId">The document it belongs to, so a reprint can find it.</param>
/// <param name="Payload">The finished byte stream, ready for the printer.</param>
/// <param name="Copies">How many times to print it.</param>
/// <param name="IsDuplicate">True for a reprint, which prints marked DUPLICATE (SRS FR-7.6).</param>
public sealed record PrintJobRequest(
    string DocType,
    long? DocId,
    byte[] Payload,
    int Copies = 1,
    bool IsDuplicate = false);
