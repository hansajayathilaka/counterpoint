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
/// <see cref="RefundMethod.CreditNote"/> is refused by every method here. Issuing a credit note
/// needs a <c>credit_note</c> row to redeem it against later, and creating that row is P2-T05 -
/// this class does not manufacture a document type it cannot back with one, in any of the three
/// return-shaped flows.
/// </para>
/// <para>
/// <see cref="RefundMethod.Exchange"/> is refused by <see cref="RequireSupported"/> too, but for a
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
    /// <paramref name="refundMethod"/> is <see cref="RefundMethod.CreditNote"/> or
    /// <see cref="RefundMethod.Exchange"/>.
    /// </exception>
    public static void RequireSupported(RefundMethod refundMethod)
    {
        if (refundMethod == RefundMethod.CreditNote)
        {
            throw new InvalidOperationException(
                "Refunding by store credit needs a credit note to issue it against, and issuing "
                + "credit notes is P2-T05. Refund by cash or card for now.");
        }

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
        RefundMethod.CreditNote => throw new InvalidOperationException("Credit note refunds are P2-T05."),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown refund method."),
    };

    public static string ToTenderType(RefundMethod method) => method switch
    {
        RefundMethod.Cash => TenderTypes.Cash,
        RefundMethod.Card => TenderTypes.Card,
        RefundMethod.CreditNote => throw new InvalidOperationException("Credit note refunds are P2-T05."),
        RefundMethod.Exchange => throw new InvalidOperationException(
            "Exchange has no payment.tender_type of its own - it is settled by the paired sale's "
            + "own tender, or, for any leftover, refunded as cash or card."),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown refund method."),
    };
}
