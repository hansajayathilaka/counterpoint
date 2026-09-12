using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One line to write onto a new <c>goods_receipt</c> (docs/01_DATA_MODEL.md §4, SRS FR-4.7,
/// FR-4.8, AC-08).
/// </summary>
/// <param name="ProductVariantId">The variant received.</param>
/// <param name="Qty">How many, in the unit it was received in (<see cref="Quantity.UomId"/>) - not the product's base unit.</param>
/// <param name="QtyBase">
/// <paramref name="Qty"/> converted to the product's base unit (AC-08) - what
/// <c>Counterpoint.Application.Abstractions.Persistence.IStockLedger.PostAsync</c> actually
/// moves. Computed once, by <c>Counterpoint.Domain.Catalogue.UomConverter.ToBase</c>, before
/// either this or <see cref="UnitCostBase"/> is built - never re-derived from <see cref="Qty"/>
/// a second time, which is exactly the AC-08 risk this line's own two quantities exist to close
/// off.
/// </param>
/// <param name="UnitCost">The supplier's price, per one of <see cref="Qty"/>'s unit, as invoiced - no freight, no tax.</param>
/// <param name="UnitCostBase">
/// The landed cost per base unit: <see cref="UnitCost"/> converted to base units plus this
/// line's own share of the receipt's freight/other cost, excluding <see cref="Tax"/>. This is
/// what <see cref="IStockLedger.PostAsync"/> receives as the movement's cost, and therefore what
/// the moving-average cost recomputes against (SRS FR-4.4, FR-4.8).
/// </param>
/// <param name="Tax">Tax on this line, entered as a plain amount - <c>goods_receipt_line</c> carries no tax-class reference of its own.</param>
/// <param name="LineTotal"><see cref="Qty"/> × <see cref="UnitCost"/>, plus this line's freight share, plus <see cref="Tax"/>.</param>
public sealed record NewGoodsReceiptLine(
    long ProductVariantId,
    Quantity Qty,
    Quantity QtyBase,
    Money UnitCost,
    Money UnitCostBase,
    Money Tax,
    Money LineTotal);
