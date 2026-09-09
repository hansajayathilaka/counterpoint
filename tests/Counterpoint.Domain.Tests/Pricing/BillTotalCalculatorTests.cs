using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.Tests.Support;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Pricing;

/// <summary>
/// A bill's totals, reconciled by construction, for both tax-exclusive and tax-inclusive shops
/// (SRS FR-3.10, task P1-T08, done-when: "Tax-inclusive and tax-exclusive bills both reconcile:
/// sum(line_total) == subtotal, subtotal - discount + tax + rounding == total").
/// </summary>
public sealed class BillTotalCalculatorTests
{
    private const int LineSetSamples = 2_000;
    private const int Seed = 20_260_908;

    private static readonly IRoundingPolicy Rounding = new HalfAwayFromZeroRounding(2);

    [Fact]
    public void FR_3_10_AnExclusiveBillReconcilesForASimpleHandWorkedExample()
    {
        // Two lines, exclusive of a 15% tax: 10.00 net (tax 1.50) and 20.00 net (tax 3.00).
        var line1 = LineTaxCalculator.Calculate(Money.FromDecimal(10m), 1m, TaxRate.FromPercent(15m), false, Rounding);
        var line2 = LineTaxCalculator.Calculate(Money.FromDecimal(20m), 1m, TaxRate.FromPercent(15m), false, Rounding);

        var totals = BillTotalCalculator.Calculate(
            [line1.LineTotal, line2.LineTotal], [line1.Tax, line2.Tax], Money.Zero, Rounding);

        totals.Subtotal.Should().Be(Money.FromDecimal(30m));
        totals.Tax.Should().Be(Money.FromDecimal(4.5m));
        totals.Total.Should().Be(Money.FromDecimal(34.5m));
        AssertReconciles(totals);
    }

    [Fact]
    public void FR_3_10_AnInclusiveBillReconcilesForASimpleHandWorkedExample()
    {
        // Two lines, inclusive of a 15% tax, gross 11.50 and 23.00: net 10.00 + 20.00, tax 1.50 + 3.00.
        var line1 = LineTaxCalculator.Calculate(Money.FromDecimal(11.5m), 1m, TaxRate.FromPercent(15m), true, Rounding);
        var line2 = LineTaxCalculator.Calculate(Money.FromDecimal(23m), 1m, TaxRate.FromPercent(15m), true, Rounding);

        var totals = BillTotalCalculator.Calculate(
            [line1.LineTotal, line2.LineTotal], [line1.Tax, line2.Tax], Money.Zero, Rounding);

        totals.Subtotal.Should().Be(Money.FromDecimal(30m));
        totals.Tax.Should().Be(Money.FromDecimal(4.5m));
        totals.Total.Should().Be(Money.FromDecimal(34.5m));
        AssertReconciles(totals);
    }

    [Fact]
    public void FR_3_10_ABillDiscountReducesTheTotalButNeverTheSubtotalOrTheTaxSum()
    {
        var line1 = LineTaxCalculator.Calculate(Money.FromDecimal(10m), 1m, TaxRate.FromPercent(15m), false, Rounding);
        var line2 = LineTaxCalculator.Calculate(Money.FromDecimal(20m), 1m, TaxRate.FromPercent(15m), false, Rounding);

        var totals = BillTotalCalculator.Calculate(
            [line1.LineTotal, line2.LineTotal], [line1.Tax, line2.Tax], Money.FromDecimal(5m), Rounding);

        totals.Subtotal.Should().Be(Money.FromDecimal(30m), "the subtotal is the sum of line totals, unaffected by a bill discount");
        totals.Tax.Should().Be(Money.FromDecimal(4.5m), "bill tax is the sum of line tax, never recomputed from the discounted total");
        totals.BillDiscount.Should().Be(Money.FromDecimal(5m));
        totals.Total.Should().Be(Money.FromDecimal(29.5m));
        AssertReconciles(totals);
    }

    [Fact]
    public void FR_3_10_MismatchedLineTotalsAndLineTaxesAreRefused()
    {
        var act = () => BillTotalCalculator.Calculate(
            [Money.FromDecimal(10m)], [Money.Zero, Money.Zero], Money.Zero, Rounding);

        act.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Done-when: "Tax-inclusive and tax-exclusive bills both reconcile: sum(line_total) ==
    /// subtotal, subtotal - discount + tax + rounding == total" - proved generatively, 1 to 50
    /// lines, both pricing modes, random unit prices, quantities and tax rates, not a fixed
    /// table of examples.
    /// </summary>
    [Fact]
    public void FR_3_10_BothPricingModesReconcileForRandomBillsOfOneToFiftyLines()
    {
        var sample = new DeterministicSample(Seed);
        var failures = new List<string>();
        var smallestBill = int.MaxValue;
        var largestBill = 0;

        for (var iteration = 0; iteration < LineSetSamples; iteration++)
        {
            var pricesIncludeTax = iteration % 2 == 0;
            var lineCount = sample.NextInt(1, 51);
            smallestBill = Math.Min(smallestBill, lineCount);
            largestBill = Math.Max(largestBill, lineCount);

            var lineTotals = new Money[lineCount];
            var lineTaxes = new Money[lineCount];
            var lineSum = Money.Zero;

            for (var i = 0; i < lineCount; i++)
            {
                var unitPrice = Money.FromDecimal(sample.NextStorableDecimal(0.01m, 10_000m));
                var quantity = sample.NextInt(1, 100);
                var taxRate = TaxRate.FromPercent(sample.NextInt(0, 26));

                var line = LineTaxCalculator.Calculate(unitPrice, quantity, taxRate, pricesIncludeTax, Rounding);
                lineTotals[i] = line.LineTotal;
                lineTaxes[i] = line.Tax;
                lineSum += line.LineTotal;
            }

            // Any discount from nothing up to the whole subtotal.
            var discount = Money.FromDecimal(sample.NextStorableDecimal(0m, Math.Max(lineSum.Amount, 0.01m)));

            var totals = BillTotalCalculator.Calculate(lineTotals, lineTaxes, discount, Rounding);

            var subtotalHolds = totals.Subtotal == lineSum;
            var identityHolds = totals.Subtotal - totals.BillDiscount + totals.Tax + totals.Rounding == totals.Total;

            if (!subtotalHolds || !identityHolds)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {iteration} ({(pricesIncludeTax ? "inclusive" : "exclusive")}, {lineCount} lines): "
                    + $"subtotal {totals.Subtotal} vs sum {lineSum}, "
                    + $"{totals.Subtotal} - {totals.BillDiscount} + {totals.Tax} + {totals.Rounding} vs total {totals.Total}"));
            }
        }

        smallestBill.Should().Be(1, "the sample must include the single-line bill");
        largestBill.Should().Be(50, "the sample must reach fifty lines");

        failures.Should().BeEmpty(
            "sum(line_total) == subtotal and subtotal - discount + tax + rounding == total must hold "
            + "for every bill shape in both pricing modes (SRS FR-3.10); seed {0}",
            Seed);
    }

    /// <summary>
    /// Done-when: "Bill discount allocation sums exactly to the discount (property test, 1-50
    /// lines)" - proved against the same bill totals <see cref="BillTotalCalculator"/> computes,
    /// not the allocator in isolation: the discount handed to <c>DiscountAllocator</c> here is
    /// exactly <see cref="BillTotals.BillDiscount"/>, and the parts it returns must still sum to
    /// it once real line totals, tax and rounding are in the picture.
    /// </summary>
    [Fact]
    public void FR_10_2_TheBillDiscountAllocatesExactlyAcrossOneToFiftyRealBillLines()
    {
        var sample = new DeterministicSample(Seed + 1);
        var failures = new List<string>();

        for (var iteration = 0; iteration < LineSetSamples; iteration++)
        {
            var pricesIncludeTax = iteration % 2 == 0;
            var lineCount = sample.NextInt(1, 51);

            var lineTotals = new Money[lineCount];
            var lineTaxes = new Money[lineCount];
            var lineSum = Money.Zero;

            for (var i = 0; i < lineCount; i++)
            {
                var unitPrice = Money.FromDecimal(sample.NextStorableDecimal(0.01m, 10_000m));
                var quantity = sample.NextInt(1, 100);
                var taxRate = TaxRate.FromPercent(sample.NextInt(0, 26));

                var line = LineTaxCalculator.Calculate(unitPrice, quantity, taxRate, pricesIncludeTax, Rounding);
                lineTotals[i] = line.LineTotal;
                lineTaxes[i] = line.Tax;
                lineSum += line.LineTotal;
            }

            var discount = Money.FromDecimal(sample.NextStorableDecimal(0m, Math.Max(lineSum.Amount, 0.01m)));
            var totals = BillTotalCalculator.Calculate(lineTotals, lineTaxes, discount, Rounding);

            // Storage-scale allocation (four decimal places), not the currency's display
            // rounding: sale.bill_discount is unrounded money (DiscountInput.ResolveAmount,
            // CLAUDE.md invariant 2) at the same 10 000 scale as sale_line.discount, so the parts
            // must sum to the discount exactly as computed here, not to a further-quantised copy
            // of it - the same granularity DiscountAllocatorTests' own property test uses.
            var allocations = DiscountAllocator.Allocate(totals.BillDiscount, lineTotals);
            var allocatedSum = allocations.Aggregate(Money.Zero, (running, part) => running + part);

            if (allocatedSum != totals.BillDiscount || allocations.Count != lineCount || allocations.Any(part => part.IsNegative))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {iteration}: {lineCount} lines, bill discount {totals.BillDiscount}, allocated {allocatedSum}"));
            }
        }

        failures.Should().BeEmpty(
            "the parts DiscountAllocator hands back for a real bill's discount must sum exactly to it, "
            + "for one to fifty lines, in both pricing modes; seed {0}",
            Seed + 1);
    }

    private static void AssertReconciles(BillTotals totals)
    {
        (totals.Subtotal - totals.BillDiscount + totals.Tax + totals.Rounding).Should().Be(totals.Total);
    }
}
