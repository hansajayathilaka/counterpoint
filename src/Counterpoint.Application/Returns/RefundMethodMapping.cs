using System;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Returns;

/// <summary>
/// The one place a <see cref="RefundMethod"/> is turned into a <c>sale_return.refund_method</c>
/// token or a <c>payment.tender_type</c>, shared by <see cref="CreateReturnHandler"/> (task
/// P2-T02), <see cref="CreateUnlinkedReturnHandler"/> (task P2-T03) and
/// <see cref="Counterpoint.Application.Exchanges.CreateExchangeHandler"/> (task P2-T04) so none of
/// the three flows can quietly disagree about what a given <see cref="RefundMethod"/> means once
/// it reaches a row.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RefundMethod.CreditNote"/> is now backed by an actual <c>credit_note</c> row
/// (task P2-T05): <see cref="CreateReturnHandler"/> and <see cref="CreateUnlinkedReturnHandler"/>
/// each issue one, in the same transaction as the return, whenever the refund method is this one -
/// this class only decides the two tokens it is written as; the row itself is
/// <c>ICreditNoteIssuer.IssueAsync</c>'s business.
/// </para>
/// <para>
/// <see cref="RefundMethod.Exchange"/> is refused by <see cref="RequireSupported"/>, for a
/// different reason: it is not a way of paying a refund out at all, it is a marker that the refund
/// was settled by a paired sale instead (<c>CreateExchangeHandler</c>'s own remarks). A standalone
/// return has no paired sale, so <see cref="CreateReturnCommand"/> and
/// <see cref="CreateUnlinkedReturnCommand"/> must never carry it -
/// <c>CreateExchangeHandler</c> is the one caller that writes <c>'EXCHANGE'</c> onto
/// <c>sale_return.refund_method</c>, and it does so directly, never through this gate.
/// </para>
/// </remarks>
internal static class RefundMethodMapping
{
    /// <exception cref="InvalidOperationException">
    /// <paramref name="refundMethod"/> is <see cref="RefundMethod.Exchange"/>.
    /// </exception>
    public static void RequireSupported(RefundMethod refundMethod)
    {
        if (refundMethod == RefundMethod.Exchange)
        {
            throw new InvalidOperationException(
                "Exchange is not a refund method a standalone return can choose - it marks a "
                + "return that is settled by a paired sale, and only an exchange has one.");
        }
    }

    public static string ToAuditToken(RefundMethod method) => method switch
    {
        RefundMethod.Cash => "CASH",
        RefundMethod.Card => "CARD",
        RefundMethod.Exchange => "EXCHANGE",
        RefundMethod.CreditNote => "CREDIT_NOTE",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown refund method."),
    };

    public static string ToTenderType(RefundMethod method) => method switch
    {
        RefundMethod.Cash => TenderTypes.Cash,
        RefundMethod.Card => TenderTypes.Card,
        RefundMethod.CreditNote => TenderTypes.CreditNote,
        RefundMethod.Exchange => throw new InvalidOperationException(
            "Exchange has no payment.tender_type of its own - it is settled by the paired sale's "
            + "own tender, or, for any leftover, refunded as cash or card."),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown refund method."),
    };
}
