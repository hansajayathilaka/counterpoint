namespace Counterpoint.Application.Sales;

/// <summary>
/// The <c>payment.tender_type</c> values Phase 1 takes (docs/01_DATA_MODEL.md §5, SRS FR-3.24).
/// </summary>
/// <remarks>
/// Constants, not an enum: they are spelled out once, here, in the words the database's own
/// <c>CHECK (tender_type IN (...))</c> constraint uses, so nothing has to spell them differently.
/// <c>CREDIT_NOTE</c> and <c>ON_ACCOUNT</c> complete the constraint's list but are not offered by
/// this task - a store-credit tender needs a credit note to redeem (P2-T05) and an on-account
/// tender needs a credit customer with a balance (P5-T02). Neither exists yet, so the payment
/// dialog (P1-T10) does not offer them; the database still accepts them the day their owning
/// tasks start writing them.
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
}
