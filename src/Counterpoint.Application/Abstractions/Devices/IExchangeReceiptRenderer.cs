namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns a completed exchange into the byte stream a receipt printer eats (SRS FR-5 exchange,
/// FR-7.1, task P2-T04).
/// </summary>
/// <remarks>
/// Pure, exactly like <see cref="ISaleReceiptRenderer"/> and <see cref="IReturnReceiptRenderer"/>:
/// bytes in, bytes out, no I/O and no device, so it is safe to call inside the exchange
/// transaction - both the return number and the bill number only exist once
/// <c>number_sequence</c> has been read twice, and the outbox row must carry the finished stream
/// (CLAUDE.md invariant 7). No printer call is ever made from here or from the transaction it runs
/// in; that is <c>IReceiptPrinter</c>'s job, from the background worker.
/// </remarks>
public interface IExchangeReceiptRenderer
{
    /// <summary>Renders one exchange receipt - one document for both halves of the exchange.</summary>
    public byte[] Render(ExchangeReceipt receipt);
}
