using System.Linq;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// The arithmetic of each canonical report definition, written once (docs/report-definitions.md
/// section 2). <see cref="PeriodFiguresReader"/> (whole-period totals) and every breakdown reader
/// (by day, by hour, by item ...) call these rather than restate the algebra, so "net sales" has
/// one formula in the report layer no matter how it is sliced (SRS FR-9.6, AC-12).
/// </summary>
/// <remarks>
/// Inputs are the stored scaled integers summed by SQL (addition only); every multiplication and
/// division happens here in <see cref="decimal"/> through <see cref="Money"/> / <see cref="Quantity"/>
/// - never scaled-times-scaled in SQL (CLAUDE.md invariant 1). Nothing here rounds: rounding is
/// for a line total and a bill total alone (invariant 2).
/// </remarks>
internal static class CanonicalFigures
{
    /// <summary>Gross sales: <c>subtotal + line_discount</c> (subtotal is already net of the line discount).</summary>
    internal static Money Gross(long subtotalScaled, long lineDiscountScaled) =>
        Money.FromScaled(subtotalScaled) + Money.FromScaled(lineDiscountScaled);

    /// <summary>Discounts: <c>line_discount + bill_discount</c>.</summary>
    internal static Money Discounts(long lineDiscountScaled, long billDiscountScaled) =>
        Money.FromScaled(lineDiscountScaled) + Money.FromScaled(billDiscountScaled);

    /// <summary>
    /// Net sales, excluding tax: gross - discounts - returns, where returns is the return's own
    /// pre-tax <c>sale_return.subtotal</c>. Reduces to <c>subtotal - bill_discount - return_subtotal</c>.
    /// </summary>
    internal static Money Net(
        long subtotalScaled,
        long lineDiscountScaled,
        long billDiscountScaled,
        long returnSubtotalScaled) =>
        Gross(subtotalScaled, lineDiscountScaled)
        - Discounts(lineDiscountScaled, billDiscountScaled)
        - Money.FromScaled(returnSubtotalScaled);

    /// <summary>
    /// The cost of a quantity at a snapshot unit cost: <c>unit_cost x qty_base</c>, multiplied here in
    /// decimal, never as a scaled product in SQL (which would double-scale).
    /// </summary>
    internal static Money LineCogs(long unitCostScaled, long qtyBaseScaled) =>
        Money.FromScaled(unitCostScaled) * Quantity.FromScaled(qtyBaseScaled, uomId: 0).Value;

    /// <summary>Gross profit: net sales - COGS.</summary>
    internal static Money GrossProfit(Money netSales, Money cogs) => netSales - cogs;

    /// <summary>Margin rate: gross profit / net sales as a fraction (0.25 = 25%); zero when net sales is zero.</summary>
    internal static decimal MarginRate(Money netSales, Money grossProfit) =>
        netSales.Amount == 0m ? 0m : grossProfit.Amount / netSales.Amount;

    /// <summary>
    /// Average bill value: discounted sales excluding tax and before returns, per bill -
    /// <c>(gross - discounts) / bill count</c>; zero with no bills.
    /// </summary>
    internal static Money AverageBillValue(Money gross, Money discounts, int billCount) =>
        billCount == 0 ? Money.Zero : (gross - discounts) / billCount;

    /// <summary>The exact sum of amounts, with no quantisation along the way.</summary>
    internal static Money Sum(System.Collections.Generic.IEnumerable<Money> amounts) =>
        amounts.Aggregate(Money.Zero, (running, amount) => running + amount);

    /// <summary>A fraction of a total (0.25 = 25%); zero when the total is zero.</summary>
    internal static decimal Share(Money part, Money total) =>
        total.Amount == 0m ? 0m : part.Amount / total.Amount;
}
