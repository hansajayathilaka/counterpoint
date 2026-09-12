using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>goods_receipt_line</c>, joined with what a screen or a print needs to show it (docs/01_DATA_MODEL.md §4).</summary>
/// <param name="Id">The line's own id.</param>
/// <param name="ProductVariantId">The variant received.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="ProductDescription">The product name, for display.</param>
/// <param name="Qty">How many, in the unit received (<see cref="Quantity.UomId"/>).</param>
/// <param name="UomSymbol">The receiving unit's display symbol.</param>
/// <param name="QtyBase">How many, converted to the product's base unit (AC-08).</param>
/// <param name="BaseUomSymbol">The product's base unit's display symbol.</param>
/// <param name="UnitCost">The supplier's price per one of <see cref="Qty"/>'s unit, as invoiced.</param>
/// <param name="UnitCostBase">The landed cost per base unit - what was fed to the stock ledger (SRS FR-4.4, FR-4.8).</param>
/// <param name="Tax">Tax on this line.</param>
/// <param name="LineTotal">This line's own total: subtotal, freight share and tax together.</param>
public sealed record GoodsReceiptLineRecord(
    long Id,
    long ProductVariantId,
    string Sku,
    string ProductDescription,
    Quantity Qty,
    string UomSymbol,
    Quantity QtyBase,
    string BaseUomSymbol,
    Money UnitCost,
    Money UnitCostBase,
    Money Tax,
    Money LineTotal);
