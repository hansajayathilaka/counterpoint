using System;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Returns;

/// <summary>
/// The one place a <see cref="RefundMethod"/> is turned into a <c>sale_return.refund_method</c>
/// token or a <c>payment.tender_type</c>, shared by <see cref="CreateReturnHandler"/> (task
/// P2-T02) and <see cref="CreateUnlinkedReturnHandler"/> (task P2-T03) so the two flows can never
/// quietly disagree about what a given <see cref="RefundMethod"/> means once it reaches a row.
/// </summary>
/// <remarks>
/// <see cref="RefundMethod.CreditNote"/> is refused by every method here. Issuing a credit note
/// needs a <c>credit_note</c> row to redeem it against later, and creating that row is P2-T05 -
/// this class does not manufacture a document type it cannot back with one, in either return flow.
/// </remarks>
internal static class RefundMethodMapping
{
    /// <exception cref="InvalidOperationException">
    /// <paramref name="refundMethod"/> is <see cref="RefundMethod.CreditNote"/>.
    /// </exception>
    public static void RequireSupported(RefundMethod refundMethod)
    {
        if (refundMethod == RefundMethod.CreditNote)
        {
            throw new InvalidOperationException(
                "Refunding by store credit needs a credit note to issue it against, and issuing "
                + "credit notes is P2-T05. Refund by cash or card for now.");
        }
    }

    public static string ToAuditToken(RefundMethod method) => method switch
    {
        RefundMethod.Cash => "CASH",
        RefundMethod.Card => "CARD",
        RefundMethod.CreditNote => throw new InvalidOperationException("Credit note refunds are P2-T05."),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown refund method."),
    };

    public static string ToTenderType(RefundMethod method) => method switch
    {
        RefundMethod.Cash => TenderTypes.Cash,
        RefundMethod.Card => TenderTypes.Card,
        RefundMethod.CreditNote => throw new InvalidOperationException("Credit note refunds are P2-T05."),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown refund method."),
    };
}
