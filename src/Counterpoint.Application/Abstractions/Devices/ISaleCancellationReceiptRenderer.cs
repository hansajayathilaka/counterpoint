namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns a cancelled bill into the byte stream a receipt printer eats (SRS FR-3.34).
/// </summary>
/// <remarks>
/// Pure, exactly like <see cref="ISaleReceiptRenderer"/>: bytes in, bytes out, no I/O and no
/// device, so it is safe to call inside the cancellation's transaction - the outbox row must
/// carry the finished stream, and the printer itself is only ever touched afterwards, by
/// <c>PrintWorker</c> (CLAUDE.md invariant 7). A fixed layout, the same deliberate choice
/// <c>EscPosSaleReceiptRenderer</c> made for the sale receipt itself: the owner-editable template
/// engine is P1-T11's, for both documents at once.
/// </remarks>
public interface ISaleCancellationReceiptRenderer
{
    /// <summary>Renders one cancellation slip.</summary>
    public byte[] Render(SaleCancellationReceipt receipt);
}
