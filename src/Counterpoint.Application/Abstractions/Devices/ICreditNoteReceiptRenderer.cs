namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// Turns a freshly issued credit note into the byte stream a receipt printer eats (SRS FR-5 store
/// credit, FR-7.1, task P2-T05).
/// </summary>
/// <remarks>
/// Pure, exactly like <see cref="IReturnReceiptRenderer"/>: bytes in, bytes out, no I/O and no
/// device, so it is safe to call inside the return transaction that issued the note - the credit
/// note number only exists once <c>number_sequence</c> has been read, and the outbox row must
/// carry the finished stream (CLAUDE.md invariant 7). No printer call is ever made from here or
/// from the transaction it runs in.
/// </remarks>
public interface ICreditNoteReceiptRenderer
{
    /// <summary>Renders one credit note document.</summary>
    public byte[] Render(CreditNoteReceipt receipt);
}
