namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns a completed return into the byte stream a receipt printer eats (SRS FR-5, FR-7.1).
/// </summary>
/// <remarks>
/// Pure, exactly like <see cref="ISaleReceiptRenderer"/>: bytes in, bytes out, no I/O and no
/// device, so it is safe to call inside the return transaction - the return number only exists
/// once <c>number_sequence</c> has been read, and the outbox row must carry the finished stream
/// (CLAUDE.md invariant 7). No printer call is ever made from here or from the transaction it
/// runs in; that is <c>IReceiptPrinter</c>'s job, from the background worker.
/// </remarks>
public interface IReturnReceiptRenderer
{
    /// <summary>Renders one return receipt.</summary>
    public byte[] Render(SaleReturnReceipt receipt);
}
