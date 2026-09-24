using System;
using System.Collections.Generic;
using System.Globalization;

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

    private static readonly HashSet<string> Accepted = new(StringComparer.Ordinal)
    {
        Cash, Card, BankTransfer, Cheque, CreditNote,
    };

    /// <summary>
    /// Refuses any tender this build cannot actually settle. <c>ON_ACCOUNT</c> in particular: the
    /// database accepts it, but with no customer account to charge (P5-T02) a payment row of that
    /// type would record goods leaving the shop against a balance nobody owes.
    /// </summary>
    /// <exception cref="InvalidOperationException">A tender type is not one of the accepted ones.</exception>
    public static void RequireAccepted(IEnumerable<string> tenderTypes)
    {
        ArgumentNullException.ThrowIfNull(tenderTypes);

        foreach (var tenderType in tenderTypes)
        {
            if (!Accepted.Contains(tenderType))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{tenderType}' is not a tender this till can take. Use cash, card, bank transfer, cheque or a credit note."));
            }
        }
    }
}
