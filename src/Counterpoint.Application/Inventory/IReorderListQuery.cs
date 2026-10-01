using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The reorder alert list (task P2-T11 "Do this" #1, SRS FR-4 reorder, FR-9.7): every active
/// product at or below its own <c>reorder_level</c>, with the shop's own reorder quantity and a
/// preferred supplier to buy it from.
/// </summary>
/// <remarks>
/// <para>
/// Not owner-only - quantity, reorder level and a supplier name carry no cost or margin figure
/// (CLAUDE.md invariant 8), the same reasoning <see cref="IStockEnquiry"/> and
/// <see cref="Counterpoint.Application.Dashboard.IDashboardQueries"/> already draw.
/// </para>
/// <para>
/// This is a distinct report from task P2-T06's own <c>ISuggestedOrderQuery</c> (the purchase
/// order screen's "start a draft order from what's low" helper, which has no supplier of its own
/// to propose - the screen already knows which supplier it is drafting for). This is the
/// operational minimum task P2-T11 describes - "the shop needs to buy stock correctly once GRN
/// exists" - built to the shape the Phase 3 report suite reuses
/// (docs/04_PHASE_2_returns_inventory.md P2-T11 "Deliverables").
/// </para>
/// </remarks>
public interface IReorderListQuery
{
    /// <summary>
    /// Every active product whose stock, summed across its active variants, is at or below its
    /// own <c>reorder_level</c> - the same predicate the dashboard's low-stock count uses
    /// (<see cref="Counterpoint.Application.Dashboard.IDashboardQueries"/>), so the two numbers
    /// never drift apart. A
    /// product whose reorder level is still the default zero is never proposed - zero means "not
    /// tracked", not "reorder immediately". Ordered by how far under the level each product sits,
    /// furthest first.
    /// </summary>
    public Task<IReadOnlyList<ReorderListLine>> GetReorderListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The same list narrowed by <paramref name="filter"/> (task P3-T06 "Do this" #7, SRS §9 RPT-10:
    /// "Supplier, category" filters). The low-stock predicate is unchanged.
    /// </summary>
    public Task<IReadOnlyList<ReorderListLine>> GetReorderListAsync(
        ReorderListFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same list as purchasing wants to read it - grouped by preferred supplier, suppliers by name
    /// with "no supplier linked" last, each group's lines furthest under their level first (SRS §9 RPT-10:
    /// "grouped by supplier").
    /// </summary>
    public Task<IReadOnlyList<ReorderSupplierGroup>> GetReorderListBySupplierAsync(
        ReorderListFilter filter,
        CancellationToken cancellationToken = default);
}

/// <summary>What the reorder list narrows to.</summary>
/// <param name="SupplierId">Only products whose <i>preferred</i> supplier (as <see cref="ReorderListLine.PreferredSupplierId"/> resolves it) is this one. Null is all.</param>
/// <param name="CategoryId">Only products filed under this category or one of its children. Null is all.</param>
public sealed record ReorderListFilter(long? SupplierId = null, long? CategoryId = null);

/// <summary>Reorder lines for one preferred supplier.</summary>
/// <param name="SupplierId">The supplier, or null for products with no supplier linked.</param>
/// <param name="SupplierName">Their name; empty when <paramref name="SupplierId"/> is null.</param>
/// <param name="Lines">The group's products, furthest under their level first.</param>
public sealed record ReorderSupplierGroup(
    long? SupplierId,
    string SupplierName,
    IReadOnlyList<ReorderListLine> Lines);

/// <summary>One product at or below its reorder level (task P2-T11 "Do this" #1).</summary>
/// <param name="ProductId">The product below its reorder level.</param>
/// <param name="ProductCode">The product's own code.</param>
/// <param name="ProductDescription">The product name.</param>
/// <param name="QtyOnHandBase">Current stock, summed across every active variant, in the product's base unit.</param>
/// <param name="ReorderLevel"><c>product.reorder_level</c>, in the product's base unit.</param>
/// <param name="SuggestedQty"><c>product.reorder_qty</c>, in the product's base unit - what to order.</param>
/// <param name="BaseUomSymbol">The product's base unit's display symbol.</param>
/// <param name="PreferredSupplierId">
/// The supplier to reorder from, chosen by the algorithm documented on
/// <c>Counterpoint.Reporting.Inventory.ReorderListQuery</c>'s own remarks, or null when the
/// product has no supplier linked at all. The product still belongs in this list even then -
/// task P2-T11's own preferred-supplier design note: "show the product in the list anyway".
/// </param>
/// <param name="PreferredSupplierName">
/// That supplier's name, or null exactly when <see cref="PreferredSupplierId"/> is null. A caller
/// displays "(none linked)" for the null case; this DTO carries no display text of its own, so a
/// later explicit "preferred supplier" flag on <c>product_supplier</c> could replace the heuristic
/// without changing this shape.
/// </param>
/// <param name="CategoryName">The product's category as filed now; empty when unfiled.</param>
public sealed record ReorderListLine(
    long ProductId,
    string ProductCode,
    string ProductDescription,
    Quantity QtyOnHandBase,
    Quantity ReorderLevel,
    Quantity SuggestedQty,
    string BaseUomSymbol,
    long? PreferredSupplierId,
    string? PreferredSupplierName,
    string CategoryName = "");
