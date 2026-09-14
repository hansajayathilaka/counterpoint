using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>What <see cref="ICreateReturn.CreateAsync"/> hands back once the return has committed.</summary>
/// <param name="SaleReturnId">The new <c>sale_return</c> row.</param>
/// <param name="ReturnNo">The allocated return number.</param>
/// <param name="TotalRefund">What was actually refunded, after the restocking fee.</param>
/// <param name="PrintJobId">The queued receipt - never printed here (CLAUDE.md invariant 7).</param>
/// <param name="CreditNoteId">
/// The new <c>credit_note</c> row, when the refund method was
/// <see cref="Counterpoint.Application.Settings.RefundMethod.CreditNote"/> (task P2-T05) - null
/// for every other refund method.
/// </param>
/// <param name="CreditNoteNumber">The allocated credit note number, or null - see <see cref="CreditNoteId"/>.</param>
/// <param name="CreditNotePrintJobId">
/// The queued credit note document - never printed here (CLAUDE.md invariant 7) - or null. A
/// second, separate outbox row from <see cref="PrintJobId"/>: the return receipt and the credit
/// note are two documents.
/// </param>
public sealed record CreatedReturn(
    long SaleReturnId,
    string ReturnNo,
    Money TotalRefund,
    long PrintJobId,
    long? CreditNoteId = null,
    string? CreditNoteNumber = null,
    long? CreditNotePrintJobId = null);
