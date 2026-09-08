using System;
using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// Totals a bill from its already-priced lines (SRS FR-3.10, task P1-T08 step 4). The same
/// arithmetic <c>Counterpoint.Application.Sales.CompleteSaleHandler</c>'s walking-skeleton
/// version inlines for its zero-discount case, generalised to a real bill discount and named so
/// the two rounding points and the reconciliation identity live in exactly one place.
/// </summary>
/// <remarks>
/// <c>sum(line_total) == subtotal</c> and <c>subtotal - bill_discount + tax + rounding == total</c>
/// hold by construction here, not by coincidence: <see cref="BillTotals.Subtotal"/> and
/// <see cref="BillTotals.Tax"/> are exact sums of the values already stored on each line, and
/// <see cref="BillTotals.Rounding"/> is derived algebraically from
/// <see cref="BillTotals.Total"/> rather than computed independently - so the four figures can
/// never drift apart even by one scaled unit (engineering guide §4.1).
/// </remarks>
public static class BillTotalCalculator
{
    /// <summary>
    /// Totals a bill.
    /// </summary>
    /// <param name="lineTotals">Every line's net total (<see cref="LinePricing.LineTotal"/>), in bill order.</param>
    /// <param name="lineTaxes">Every line's tax (<see cref="LinePricing.Tax"/>), in the same order.</param>
    /// <param name="billDiscount">
    /// The whole-bill discount, already resolved to money (<see cref="DiscountEvaluation.Amount"/>) -
    /// <see cref="Money.Zero"/> when there is none. Allocating it back across the lines, so each
    /// <c>sale_line.discount</c> is set, is
    /// <c>Counterpoint.Domain.Services.DiscountAllocator</c>'s job, not this one (task P1-T08
    /// step 3) - this only needs the total to total the bill.
    /// </param>
    /// <param name="rounding">The bill-total rounding point (CLAUDE.md invariant 2).</param>
    /// <exception cref="ArgumentException"><paramref name="lineTotals"/> and <paramref name="lineTaxes"/> have different lengths.</exception>
    public static BillTotals Calculate(
        IReadOnlyList<Money> lineTotals,
        IReadOnlyList<Money> lineTaxes,
        Money billDiscount,
        IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(lineTotals);
        ArgumentNullException.ThrowIfNull(lineTaxes);
        ArgumentNullException.ThrowIfNull(rounding);

        if (lineTotals.Count != lineTaxes.Count)
        {
            throw new ArgumentException(
                "There must be exactly one tax figure per line total.",
                nameof(lineTaxes));
        }

        var subtotal = lineTotals.Aggregate(Money.Zero, (running, line) => running + line);
        var tax = lineTaxes.Aggregate(Money.Zero, (running, line) => running + line);

        // Rounding point two.
        var total = rounding.Round(subtotal - billDiscount + tax);

        // Derived from the scaled quantities, not decimal subtraction: rounding is the figure
        // that makes the row as stored balance, so it is computed in the arithmetic the row is
        // stored in (the same reasoning CompleteSaleHandler's skeleton documents at its own
        // rounding step).
        var roundingAdjustment = Money.FromScaled(
            total.ToScaled() - subtotal.ToScaled() + billDiscount.ToScaled() - tax.ToScaled());

        return new BillTotals(subtotal, billDiscount, tax, roundingAdjustment, total);
    }
}

/// <summary>A bill's totals, reconciled by construction (SRS FR-3.10).</summary>
/// <param name="Subtotal"><c>sale.subtotal</c> - the exact sum of every line's net total.</param>
/// <param name="BillDiscount"><c>sale.bill_discount</c>.</param>
/// <param name="Tax"><c>sale.tax</c> - the exact sum of every line's tax, never recomputed from the total.</param>
/// <param name="Rounding"><c>sale.rounding</c> - what the bill-total rounding step moved.</param>
/// <param name="Total"><c>sale.total</c> - what the customer pays.</param>
public sealed record BillTotals(Money Subtotal, Money BillDiscount, Money Tax, Money Rounding, Money Total);
