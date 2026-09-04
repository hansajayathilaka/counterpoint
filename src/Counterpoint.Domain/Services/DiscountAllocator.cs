using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Services;

/// <summary>
/// Spreads a bill-level discount across the lines of the bill, proportionally to each line's
/// total, by the largest-remainder method.
///
/// The point of the exercise is that the parts sum <strong>exactly</strong> to the discount.
/// Allocating proportionally and rounding each part independently loses or invents a minor
/// unit whenever the split does not divide evenly, and that difference goes on to break
/// <c>subtotal - bill_discount + tax + rounding == total</c> — the reconciliation invariant
/// the domain asserts before a sale is persisted. So the parts are floored, and the residual
/// units are handed out one at a time to the lines with the largest fractional remainder,
/// ties going to the largest line.
///
/// All arithmetic is <see cref="decimal"/> and scaled integers. Nothing here rounds in the
/// sense of CLAUDE.md invariant 2: the line totals arriving here have already been rounded at
/// the line-total rounding point, and choosing which line carries a residual unit is an
/// allocation decision, not a third rounding point.
/// </summary>
public static class DiscountAllocator
{
    private static readonly long[] PowersOfTen = [1L, 10L, 100L, 1_000L, 10_000L];

    /// <summary>
    /// Allocates at the storage granularity — one ten-thousandth of a currency unit, the
    /// finest amount the database can hold.
    /// </summary>
    /// <param name="discount">The bill-level discount. Must not be negative.</param>
    /// <param name="lineTotals">The line totals to spread it over. Must not be negative.</param>
    /// <returns>One part per line, in the same order, summing exactly to the discount.</returns>
    public static IReadOnlyList<Money> Allocate(Money discount, IReadOnlyList<Money> lineTotals) =>
        Allocate(discount, lineTotals, Money.MoneyDecimalPlaces);

    /// <summary>
    /// Allocates at the granularity of the currency's minor unit, so that every part is an
    /// amount the shop could actually print and hand over.
    /// </summary>
    public static IReadOnlyList<Money> Allocate(
        Money discount,
        IReadOnlyList<Money> lineTotals,
        IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(rounding);

        return Allocate(discount, lineTotals, rounding.DecimalPlaces);
    }

    /// <summary>
    /// Allocates in units of <c>10^-decimalPlaces</c>.
    /// </summary>
    /// <param name="discount">
    /// The bill-level discount. Must not be negative. It is first quantised to
    /// <paramref name="decimalPlaces"/> half away from zero; the parts then sum exactly to
    /// that quantised amount. At the default storage granularity every stored discount is
    /// already exactly representable, so the parts sum exactly to the discount as given.
    /// </param>
    /// <param name="lineTotals">The line totals to spread it over. Must not be negative.</param>
    /// <param name="decimalPlaces">Granularity, 0 to <see cref="Money.MoneyDecimalPlaces"/>.</param>
    /// <returns>One part per line, in the same order.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="lineTotals"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// There are no lines, a line total is negative, or the lines total zero while the
    /// discount does not — there is nothing to be proportional to.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The discount is negative, or <paramref name="decimalPlaces"/> is out of range.
    /// </exception>
    public static IReadOnlyList<Money> Allocate(
        Money discount,
        IReadOnlyList<Money> lineTotals,
        int decimalPlaces)
    {
        ArgumentNullException.ThrowIfNull(lineTotals);
        ScaledDecimal.RequireStorablePlaces(decimalPlaces, nameof(decimalPlaces));

        if (lineTotals.Count == 0)
        {
            throw new ArgumentException(
                "A bill discount needs at least one line to be allocated to.",
                nameof(lineTotals));
        }

        if (discount.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(discount),
                discount.Amount,
                "A bill discount is an amount taken off, so it cannot be negative. "
                + "A negative adjustment is a surcharge, and that is not this.");
        }

        var lineCount = lineTotals.Count;
        var unitFactor = PowersOfTen[Money.MoneyDecimalPlaces - decimalPlaces];
        var discountUnits = ToUnits(discount, decimalPlaces, unitFactor);
        var allocations = new Money[lineCount];

        var lineSum = 0m;
        for (var i = 0; i < lineCount; i++)
        {
            var lineTotal = lineTotals[i];

            if (lineTotal.IsNegative)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"Line {i} has a negative total ({lineTotal}). A discount cannot be "
                        + $"allocated proportionally across negative lines."),
                    nameof(lineTotals));
            }

            lineSum += lineTotal.Amount;
            allocations[i] = Money.Zero;
        }

        if (discountUnits == 0L)
        {
            return allocations;
        }

        if (lineSum == 0m)
        {
            throw new ArgumentException(
                "The lines total zero, so a non-zero bill discount cannot be spread across "
                + "them proportionally.",
                nameof(lineTotals));
        }

        var units = new long[lineCount];
        var remainders = new decimal[lineCount];
        var allocatedUnits = 0L;

        for (var i = 0; i < lineCount; i++)
        {
            // The share is taken as discount * (line / sum) rather than (discount * line) / sum:
            // the ratio is never above one, so the product cannot overflow decimal even for
            // amounts at the top of the storable range. The lost precision is far below one
            // unit, and the residual pass below makes the sum exact regardless.
            var exactShare = discountUnits * (lineTotals[i].Amount / lineSum);
            var wholeUnits = decimal.Floor(exactShare);

            units[i] = decimal.ToInt64(wholeUnits);
            remainders[i] = exactShare - wholeUnits;
            allocatedUnits += units[i];
        }

        DistributeResidual(discountUnits - allocatedUnits, units, remainders, lineTotals);

        for (var i = 0; i < lineCount; i++)
        {
            allocations[i] = Money.FromScaled(units[i] * unitFactor);
        }

        return allocations;
    }

    /// <summary>
    /// Hands the leftover units out one at a time, largest fractional remainder first, ties
    /// going to the largest line. This is what makes the parts sum exactly.
    /// </summary>
    private static void DistributeResidual(
        long residualUnits,
        long[] units,
        decimal[] remainders,
        IReadOnlyList<Money> lineTotals)
    {
        if (residualUnits == 0L)
        {
            return;
        }

        var order = Enumerable.Range(0, units.Length)
            .OrderByDescending(i => remainders[i])
            .ThenByDescending(i => lineTotals[i].Amount)
            .ThenBy(i => i)
            .ToArray();

        var step = residualUnits > 0L ? 1L : -1L;

        for (var handedOut = 0L; handedOut < Math.Abs(residualUnits); handedOut++)
        {
            units[order[(int)(handedOut % order.Length)]] += step;
        }
    }

    private static long ToUnits(Money amount, int decimalPlaces, long unitFactor)
    {
        var quantised = decimal.Round(amount.Amount, decimalPlaces, MidpointRounding.AwayFromZero);

        return Money.FromDecimal(quantised).ToScaled() / unitFactor;
    }
}
