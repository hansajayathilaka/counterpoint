using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One product at or below its reorder level, with the shop's own suggested reorder quantity
/// (SRS FR-4.6, task P2-T06 "Do this" #3: the stock balance projection's quantity on hand falling
/// below <c>product.reorder_level</c> proposes <c>product.reorder_qty</c>).
/// </summary>
/// <param name="ProductId">The product below its reorder level.</param>
/// <param name="ProductCode">The product's own code.</param>
/// <param name="ProductDescription">The product name.</param>
/// <param name="QtyOnHandBase">
/// Current stock, summed across every active variant, in the product's base unit - the same
/// aggregation <c>SqliteDashboardReader.GetLowStockCountAsync</c> uses for the dashboard's count
/// of this same product.
/// </param>
/// <param name="ReorderLevel"><c>product.reorder_level</c>, in the product's base unit.</param>
/// <param name="SuggestedQty"><c>product.reorder_qty</c>, in the product's base unit - what to order.</param>
/// <param name="BaseUomSymbol">The product's base unit's display symbol.</param>
public sealed record SuggestedOrderLine(
    long ProductId,
    string ProductCode,
    string ProductDescription,
    Quantity QtyOnHandBase,
    Quantity ReorderLevel,
    Quantity SuggestedQty,
    string BaseUomSymbol);
