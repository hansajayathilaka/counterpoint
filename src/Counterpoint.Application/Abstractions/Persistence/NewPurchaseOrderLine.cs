using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One line to write onto a new <c>purchase_order</c> (docs/01_DATA_MODEL.md §4, SRS FR-4.5).
/// </summary>
/// <param name="ProductVariantId">The variant being ordered.</param>
/// <param name="Qty">
/// How many, in the unit it was ordered in (<see cref="Quantity.UomId"/>) - not the product's
/// base unit. <c>purchase_order_line.qty</c> is deliberately stored in the ordering unit; the
/// conversion to base units happens at goods receipt (P2-T07), never here.
/// </param>
/// <param name="UnitCost">The expected cost per one of <see cref="Qty"/>'s unit.</param>
public sealed record NewPurchaseOrderLine(long ProductVariantId, Quantity Qty, Money UnitCost);
