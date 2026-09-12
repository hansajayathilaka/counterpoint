using System;
using System.Collections.Generic;
using System.Linq;

namespace Counterpoint.Domain.Purchasing;

/// <summary>
/// Derives a purchase order's status from how much of it has been received (SRS FR-4.10, P2-T06
/// "Do this" #2).
/// </summary>
/// <remarks>
/// <para>
/// Pure and I/O-free, on purpose: <c>Counterpoint.Application.Purchasing.IPurchaseOrderService</c>
/// reads the order's lines and their receipt progress, converts each ordered quantity to the same
/// base unit its <c>qty_received_base</c> is already in
/// (<c>Counterpoint.Domain.Catalogue.UomConverter.ToBase</c>), and hands the result here. This
/// class never touches the database and never resolves a UOM conversion factor itself, which is
/// what lets a status-transition test build one directly, with plain <see cref="Domain.ValueObjects.Quantity"/>
/// values and no product, no store and no goods receipt in sight.
/// </para>
/// <para>
/// <b>Not a decision about <see cref="PurchaseOrderStatus.Draft"/> or
/// <see cref="PurchaseOrderStatus.Cancelled"/>.</b> Those two are explicit operator actions
/// (raising a draft, sending it, cancelling it) that this calculator has no business overriding -
/// it only ever answers <see cref="PurchaseOrderStatus.Sent"/>, <see cref="PurchaseOrderStatus.Partial"/>
/// or <see cref="PurchaseOrderStatus.Received"/>, and the caller is the one that decides whether
/// the order is even in a state this recomputation applies to (P2-T06's "Do this" #2: the
/// lifecycle is DRAFT -&gt; SENT -&gt; PARTIAL -&gt; RECEIVED | CANCELLED, and only the middle three
/// steps are receipt-progress-driven).
/// </para>
/// </remarks>
public static class PurchaseOrderStatusCalculator
{
    /// <summary>
    /// Derives <see cref="PurchaseOrderStatus.Sent"/>, <see cref="PurchaseOrderStatus.Partial"/> or
    /// <see cref="PurchaseOrderStatus.Received"/> from every line's receipt progress (FR-4.10):
    /// nothing received on any line keeps it <see cref="PurchaseOrderStatus.Sent"/>; every line
    /// fully received makes it <see cref="PurchaseOrderStatus.Received"/>; anything between the
    /// two - one line touched but not every line finished - is <see cref="PurchaseOrderStatus.Partial"/>.
    /// </summary>
    /// <param name="lines">Every line on the order. Never empty - a purchase order always has at least one line.</param>
    /// <exception cref="ArgumentException"><paramref name="lines"/> is null or empty.</exception>
    public static PurchaseOrderStatus DeriveFromReceiptProgress(
        IReadOnlyList<PurchaseOrderLineReceiptProgress> lines)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException(
                "A purchase order's status cannot be derived with no lines to derive it from.",
                nameof(lines));
        }

        var anyReceived = lines.Any(line => line.ReceivedBase.Value > 0m);
        var everyLineFullyReceived = lines.All(line => line.ReceivedBase.Value >= line.OrderedBase.Value);

        if (everyLineFullyReceived)
        {
            return PurchaseOrderStatus.Received;
        }

        return anyReceived ? PurchaseOrderStatus.Partial : PurchaseOrderStatus.Sent;
    }
}
