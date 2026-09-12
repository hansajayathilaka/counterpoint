using System;
using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Purchasing;

/// <summary>
/// Spreads a goods receipt's freight/other cost across its lines, in proportion to each line's
/// own subtotal (SRS FR-4.7, FR-4.8, P2-T07 "Do this" #3).
/// </summary>
/// <remarks>
/// <para>
/// Pure and I/O-free, the same shape as <see cref="PurchaseOrderStatusCalculator"/>: everything
/// it needs is already in memory, so the "the shares sum to exactly the entered freight" property
/// is provable with a hand-worked example, not just observed on a lucky run.
/// </para>
/// <para>
/// <b>Why a plain proportional split does not already sum exactly.</b> Dividing
/// <c>other_cost</c> by each line's share of the total subtotal is an exact rational number that
/// almost never lands on a whole scaled unit (a ten-thousandth of the currency) for every line at
/// once; naively rounding each share independently can land the total a scaled unit or two away
/// from what was entered. This uses the largest-remainder method instead: every share is floored
/// to a whole scaled unit first, and whatever is left over from the floor - always fewer scaled
/// units than there are lines - is handed out one unit at a time to the lines whose exact share
/// had the largest fractional part, so the total is exact by construction, not by luck.
/// </para>
/// </remarks>
public static class GoodsReceiptFreightApportioner
{
    /// <summary>
    /// Apportions <paramref name="otherCost"/> across <paramref name="lineSubtotals"/>, in the
    /// same order, so that the returned shares sum to exactly <paramref name="otherCost"/>.
    /// </summary>
    /// <param name="otherCost">The freight or other cost entered on the receipt. Never negative.</param>
    /// <param name="lineSubtotals">
    /// Each line's own pre-freight, pre-tax subtotal (<c>qty × unit_cost</c>), in the order the
    /// shares should come back in. Never empty.
    /// </param>
    /// <returns>
    /// One share per entry of <paramref name="lineSubtotals"/>, in the same order, summing to
    /// exactly <paramref name="otherCost"/> once every <see cref="Money.ToScaled"/> is added.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="lineSubtotals"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="otherCost"/> is negative.</exception>
    public static IReadOnlyList<Money> Apportion(Money otherCost, IReadOnlyList<Money> lineSubtotals)
    {
        ArgumentNullException.ThrowIfNull(lineSubtotals);

        if (lineSubtotals.Count == 0)
        {
            throw new ArgumentException(
                "At least one line is needed to apportion a freight cost across.",
                nameof(lineSubtotals));
        }

        if (otherCost.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(otherCost),
                otherCost,
                "A freight/other-cost amount cannot be negative.");
        }

        var totalScaled = otherCost.ToScaled();

        if (totalScaled == 0)
        {
            return [.. lineSubtotals.Select(_ => Money.Zero)];
        }

        // A negative line subtotal cannot happen in practice (a GRN line's cost is never
        // negative), but weighting by it would be nonsensical if it ever did - floored at zero
        // rather than trusted blindly.
        var weights = lineSubtotals.Select(subtotal => Math.Max(subtotal.ToScaled(), 0L)).ToArray();
        var weightSum = weights.Sum();

        if (weightSum == 0)
        {
            // Every line costs nothing (a free sample on the same invoice, for instance) - there
            // is no "share of value" to divide by, so the freight is split evenly by count
            // instead of dividing by zero.
            weights = [.. Enumerable.Repeat(1L, lineSubtotals.Count)];
            weightSum = lineSubtotals.Count;
        }

        var shares = new long[lineSubtotals.Count];
        var remainders = new decimal[lineSubtotals.Count];
        var allocated = 0L;

        for (var i = 0; i < lineSubtotals.Count; i++)
        {
            // Exact decimal division before flooring, so the remainder driving the leftover
            // distribution below is the true fractional part, not itself a rounded value.
            var exact = (decimal)totalScaled * weights[i] / weightSum;
            var floor = Math.Floor(exact);
            shares[i] = (long)floor;
            remainders[i] = exact - floor;
            allocated += shares[i];
        }

        var leftover = totalScaled - allocated;

        foreach (var index in Enumerable.Range(0, lineSubtotals.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i))
        {
            if (leftover <= 0)
            {
                break;
            }

            shares[index] += 1;
            leftover -= 1;
        }

        return [.. shares.Select(Money.FromScaled)];
    }
}
