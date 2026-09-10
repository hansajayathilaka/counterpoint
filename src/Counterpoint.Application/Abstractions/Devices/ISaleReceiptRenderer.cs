namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns a completed bill into the byte stream a receipt printer eats (SRS FR-7.1).
/// </summary>
/// <remarks>
/// <para>
/// Rendering is pure: bytes in, bytes out, no I/O and no device. It is therefore safe to call
/// inside the sale transaction, which is where it has to happen - the bill number only exists
/// once <c>number_sequence</c> has been read, and the outbox row must carry the finished stream
/// (docs/01_DATA_MODEL.md §8). What is never done inside a transaction is calling the printer;
/// that is <see cref="IReceiptPrinter"/>'s job, from the background worker (CLAUDE.md
/// invariant 7).
/// </para>
/// <para>
/// The layout itself lives in Counterpoint.Devices. Owner-editable templates are P1-T11; the
/// skeleton renders a fixed one.
/// </para>
/// </remarks>
public interface ISaleReceiptRenderer
{
    /// <summary>Renders one bill.</summary>
    /// <param name="receipt">What to print.</param>
    /// <param name="isDuplicate">
    /// True for a reprint (SRS FR-7.5, FR-7.6): the printed copy carries a "DUPLICATE" line and
    /// the caller is expected to have logged the reprint separately (CLAUDE.md invariant 5 - an
    /// <c>audit_log</c> row, never a flag on the original <c>sale</c>).
    /// </param>
    public byte[] Render(SaleReceipt receipt, bool isDuplicate = false);
}
