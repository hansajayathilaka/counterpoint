using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>purchase_order_line</c>, joined with what a screen needs to show it (docs/01_DATA_MODEL.md §4).</summary>
/// <param name="Id">The line's own id.</param>
/// <param name="ProductVariantId">The variant ordered.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="ProductDescription">The product name, for display - not a CLAUDE.md invariant 10 snapshot: this row is ordinary and mutable, not append-only.</param>
/// <param name="Qty">How many, in the unit ordered (<see cref="Quantity.UomId"/>).</param>
/// <param name="UomSymbol">The ordering unit's display symbol.</param>
/// <param name="UnitCost">The expected cost per one of <see cref="Qty"/>'s unit.</param>
/// <param name="LineTotal"><see cref="Qty"/> times <see cref="UnitCost"/>.</param>
/// <param name="QtyReceivedBase">
/// <c>purchase_order_line.qty_received_base</c> - in the product's base unit, raised as goods
/// receipts land against the order (P2-T07). Zero until then.
/// </param>
/// <param name="BaseUomSymbol">The product's base unit's display symbol, for showing <see cref="QtyReceivedBase"/>.</param>
public sealed record PurchaseOrderLineRecord(
    long Id,
    long ProductVariantId,
    string Sku,
    string ProductDescription,
    Quantity Qty,
    string UomSymbol,
    Money UnitCost,
    Money LineTotal,
    Quantity QtyReceivedBase,
    string BaseUomSymbol);
