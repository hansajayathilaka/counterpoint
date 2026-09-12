using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Purchasing;

/// <summary>
/// Raising, sending, cancelling and printing purchase orders, and the suggested-order report
/// (SRS FR-4.5, FR-4.6, FR-4.10). Owner only (SRS §3.3 ROLE-2's "purchasing", AC-17) - the same
/// reasoning <see cref="Counterpoint.Application.Catalogue.ISupplierMaintenance"/> already
/// carries for the supplier records a purchase order references.
/// </summary>
/// <remarks>
/// Deliberately light (P2-T06's own "Risks": "Purchase orders are useful but not load-bearing -
/// the GRN is what changes stock"). Nothing here posts a stock movement or touches the stock
/// balance projection; <see cref="RecomputeStatusAsync"/> exists only so the goods-receipt
/// handler (P2-T07) has one call to make after it updates <c>purchase_order_line.qty_received_base</c>,
/// without either task needing to reach into the other's transaction.
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IPurchaseOrderService
{
    /// <summary>Every purchase order, newest first.</summary>
    public Task<IReadOnlyList<PurchaseOrderSummaryRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>One purchase order with its lines, or null when it does not exist.</summary>
    public Task<PurchaseOrderRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raises a new purchase order in <c>DRAFT</c>, with its number allocated and every line
    /// validated against the product it names (FR-4.5, CLAUDE.md invariant 4).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The supplier does not exist, there are no lines, a line's product does not sell in the
    /// unit named, or a line's quantity is not one that unit's own rules allow (FR-2.1-FR-2.8).
    /// </exception>
    public Task<PurchaseOrderRecord> CreateAsync(
        CreatePurchaseOrderCommand command,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a <c>DRAFT</c> order <c>SENT</c> - the order has gone to the supplier and can no
    /// longer be freely edited.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">The order does not exist or is not <c>DRAFT</c>.</exception>
    public Task SendAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels an order that has not been fully received (FR-4.5's lifecycle). The order keeps
    /// its number (CLAUDE.md invariant 4) and nothing here touches stock - cancelling before
    /// anything was received changes nothing that a goods receipt would have changed anyway.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The order does not exist, or is already <c>RECEIVED</c> or <c>CANCELLED</c>.
    /// </exception>
    public Task CancelAsync(long id, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renders the order as a PDF and queues it in the print outbox (CLAUDE.md invariant 7).
    /// Returns the outbox row id.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">The order does not exist.</exception>
    public Task<long> PrintAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes <c>SENT</c>/<c>PARTIAL</c>/<c>RECEIVED</c> from the order's current receipt
    /// progress (<see cref="Domain.Purchasing.PurchaseOrderStatusCalculator"/>) and persists it.
    /// Called by the goods-receipt handler (P2-T07) after it updates
    /// <c>purchase_order_line.qty_received_base</c>; a no-op for an order that is <c>DRAFT</c> or
    /// <c>CANCELLED</c>, since receipt progress never drives either of those two.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">The order does not exist.</exception>
    public Task RecomputeStatusAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The suggested-order report (FR-4.6): every product at or below its reorder level.</summary>
    public Task<IReadOnlyList<SuggestedOrderLine>> GetSuggestedOrderAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every unit a variant's product may be ordered in, base unit included (FR-2.4, FR-2.5) -
    /// what the purchase order screen offers for a line once a product has been picked.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">The variant does not exist.</exception>
    public Task<IReadOnlyList<ProductUomRecord>> GetOrderingUnitsAsync(
        long productVariantId,
        CancellationToken cancellationToken = default);
}
