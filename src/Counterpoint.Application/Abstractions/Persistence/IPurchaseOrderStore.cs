using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.Purchasing;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>Reads and writes <c>purchase_order</c> and <c>purchase_order_line</c> (docs/01_DATA_MODEL.md §4, SRS FR-4.5, FR-4.10).</summary>
/// <remarks>
/// Neither table is append-only or hash-chained (CLAUDE.md invariant 5 names exactly which
/// tables are; these two are not among them) - a purchase order's <c>status</c> is an ordinary
/// mutable column, updated by <see cref="UpdateStatusAsync"/> as it moves through its lifecycle.
/// The GRN that actually changes stock is P2-T07's, through <c>StockLedger.PostAsync</c>; nothing
/// here posts a stock movement or touches the stock balance projection.
/// </remarks>
public interface IPurchaseOrderStore
{
    /// <summary>Every purchase order, newest first - the list screen's whole read side.</summary>
    public Task<IReadOnlyList<PurchaseOrderSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One purchase order with its lines, or null when it does not exist.</summary>
    public Task<PurchaseOrderRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the order and every one of its lines together, in the caller's transaction - so an
    /// order can never exist with no lines (docs/01_DATA_MODEL.md §4).
    /// </summary>
    public Task<long> CreateAsync(NewPurchaseOrder order, CancellationToken cancellationToken = default);

    /// <summary>Sets <c>purchase_order.status</c> to a new <see cref="PurchaseOrderStatuses"/> token.</summary>
    public Task UpdateStatusAsync(long id, PurchaseOrderStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every line's ordered quantity against what has been received so far, both converted to the
    /// line's product's base unit - <see cref="Domain.Purchasing.PurchaseOrderStatusCalculator"/>'s
    /// whole input (SRS FR-4.10).
    /// </summary>
    public Task<IReadOnlyList<PurchaseOrderLineReceiptProgress>> FindReceiptProgressAsync(
        long id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds <paramref name="deltaBase"/> to <c>qty_received_base</c> on the one line of
    /// <paramref name="purchaseOrderId"/> that orders <paramref name="productVariantId"/> - the
    /// goods-receipt handler's own write (P2-T07, SRS FR-4.10). When more than one line on the
    /// same order names the same variant (unusual, but the schema does not forbid it), the first
    /// such line by id receives the whole increment.
    /// </summary>
    /// <returns>
    /// False, having written nothing, when the order has no line for that variant - a GRN item
    /// outside what was ordered still posts its own stock movement; there is simply nothing on
    /// the order for it to advance.
    /// </returns>
    public Task<bool> IncrementReceivedAsync(
        long purchaseOrderId,
        long productVariantId,
        Quantity deltaBase,
        CancellationToken cancellationToken = default);
}
