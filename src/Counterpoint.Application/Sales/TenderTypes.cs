namespace Counterpoint.Application.Sales;

/// <summary>
/// The <c>payment.tender_type</c> values Phase 1 takes (docs/01_DATA_MODEL.md §5, SRS FR-3.24).
/// </summary>
/// <remarks>
/// Constants, not an enum: they are spelled out once, here, in the words the database's own
/// <c>CHECK (tender_type IN (...))</c> constraint uses, so nothing has to spell them differently.
/// <c>ON_ACCOUNT</c> completes the constraint's list but is not offered by this task - an
/// on-account tender needs a credit customer with a balance (P5-T02), which does not exist yet,
/// so the payment dialog (P1-T10) does not offer it; the database still accepts it the day that
/// task starts writing it. <see cref="CreditNote"/> is now backed by an actual
/// <c>credit_note</c> row (task P2-T05, <see cref="Counterpoint.Application.Returns.RefundMethodMapping"/>
/// issues one; <c>CompleteSaleHandler</c> redeems one).
/// </remarks>
public static class TenderTypes
{
    /// <summary>Notes and coins. The only tender that can be offered for more than is owed - the
    /// rest is change (SRS FR-3.26).</summary>
    public const string Cash = "CASH";

    /// <summary>A debit or credit card, taken on a separate card terminal.</summary>
    public const string Card = "CARD";

    /// <summary>A bank or mobile money transfer.</summary>
    public const string BankTransfer = "BANK_TRANSFER";

    /// <summary>A cheque.</summary>
    public const string Cheque = "CHEQUE";

    /// <summary>
    /// Store credit, redeemed against a numbered <c>credit_note</c> row (SRS FR-5 store credit,
    /// FR-3 tender, task P2-T05). <see cref="TenderRequest.Reference"/> carries the credit note's
    /// own <c>number</c> - the convention <c>CompleteSaleHandler</c> uses to find which note a
    /// tender of this type is spending.
    /// </summary>
    public const string CreditNote = "CREDIT_NOTE";
}
