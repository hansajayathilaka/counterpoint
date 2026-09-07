namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// A queued document the print worker has picked up.
/// </summary>
/// <param name="Id">The outbox row id.</param>
/// <param name="DocType">A <c>print_job.doc_type</c> value.</param>
/// <param name="DocId">The document it belongs to.</param>
/// <param name="Payload">The finished byte stream.</param>
/// <param name="Copies">How many times to print it.</param>
/// <param name="Attempts">How many times it has already been tried and failed.</param>
public sealed record PendingPrintJob(
    long Id,
    string DocType,
    long? DocId,
    byte[] Payload,
    int Copies,
    int Attempts);
