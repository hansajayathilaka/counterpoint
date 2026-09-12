using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One line of a return about to be written (task P2-T02).</summary>
/// <remarks>
/// <see cref="UnitPrice"/> and <see cref="UnitCost"/> are copied verbatim from the original
/// <c>sale_line</c> row, never recomputed and never looked up again (CLAUDE.md invariant 10,
/// SRS AC-03) - they are documentary, the same "what one selling unit cost" fact the bill itself
/// carried. <see cref="LineRefund"/> and <see cref="Tax"/> are the money that actually moves,
/// prorated from the original line's own <c>line_total</c> and <c>tax</c> by
/// <c>QuantityBase / (the line's original qty_base)</c>, so a partial return of a discounted line
/// refunds exactly its share of the discount that was applied (AC-03) - never a fresh multiply of
/// <see cref="UnitPrice"/> by <see cref="QuantityBase"/>, which would silently drop the discount.
/// </remarks>
/// <param name="SaleLineId">The original line this reverses. Null only for an unlinked return (P2-T03).</param>
/// <param name="ProductVariantId">The variant returned.</param>
/// <param name="QuantityBase">How much is coming back, in base units. Always positive.</param>
/// <param name="UnitPrice">The price ORIGINALLY paid, copied from <c>sale_line.unit_price</c>.</param>
/// <param name="UnitCost">Cost per base unit at the moment of sale, copied from <c>sale_line.unit_cost</c>.</param>
/// <param name="Tax">This line's prorated share of the tax originally charged.</param>
/// <param name="LineRefund">This line's prorated refund, net of tax and before the restocking fee.</param>
/// <param name="Reason">The cashier's reason for this line (SRS FR-5.7).</param>
/// <param name="Disposition">One of the <c>sale_return_line.disposition</c> tokens (SRS FR-5.8).</param>
public sealed record NewSaleReturnLine(
    long? SaleLineId,
    long ProductVariantId,
    Quantity QuantityBase,
    Money UnitPrice,
    Money UnitCost,
    Money Tax,
    Money LineRefund,
    string Reason,
    string Disposition);
