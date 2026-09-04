using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.Tests.Support;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Services;

/// <summary>
/// A bill-level discount spread across lines must sum back to itself exactly, or the
/// reconciliation invariant <c>subtotal - bill_discount + tax + rounding == total</c> breaks
/// (docs/00_ENGINEERING_GUIDE.md §4.1, SRS FR-10.2).
/// </summary>
public sealed class DiscountAllocatorTests
{
    /// <summary>Random line sets to try. Each one is a separate bill shape.</summary>
    private const int LineSetSamples = 2_000;

    private const int Seed = 20_260_906;

    [Fact]
    public void FR_10_2_AllocationSumsExactlyToTheDiscountForRandomLineSetsOfOneToFifty()
    {
        var sample = new DeterministicSample(Seed);
        var failures = new List<string>();
        var smallestLineSet = int.MaxValue;
        var largestLineSet = 0;
        var setsNeedingAResidual = 0;

        for (var iteration = 0; iteration < LineSetSamples; iteration++)
        {
            // 1 to 50 lines, each between 0.0001 and 10 000.0000 currency units.
            var lineCount = sample.NextInt(1, 51);
            var lineTotals = new Money[lineCount];
            var lineScaled = new long[lineCount];
            var lineSumScaled = 0L;

            for (var i = 0; i < lineCount; i++)
            {
                var scaled = sample.NextScaled(1L, 100_000_001L);
                lineScaled[i] = scaled;
                lineTotals[i] = Money.FromScaled(scaled);
                lineSumScaled += scaled;
            }

            // Any discount from nothing up to the whole bill.
            var discountScaled = sample.NextScaled(0L, lineSumScaled + 1L);
            var discount = Money.FromScaled(discountScaled);

            smallestLineSet = Math.Min(smallestLineSet, lineCount);
            largestLineSet = Math.Max(largestLineSet, lineCount);
            if (ResidualUnits(discountScaled, lineScaled, lineSumScaled) > 0L)
            {
                setsNeedingAResidual++;
            }

            var allocations = DiscountAllocator.Allocate(discount, lineTotals);
            var allocated = allocations.Aggregate(Money.Zero, (running, part) => running + part);

            if (allocated != discount || allocations.Count != lineCount ||
                allocations.Any(part => part.IsNegative))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {iteration}: {lineCount} lines, discount {discount}, "
                    + $"allocated {allocated}"));
            }
        }

        smallestLineSet.Should().Be(1, "the sample must include the single-line bill");
        largestLineSet.Should().Be(50, "the sample must reach fifty lines");
        setsNeedingAResidual.Should().BeGreaterThan(LineSetSamples / 2,
            "if the proportional split always divided evenly the largest-remainder branch "
            + "would never run and this property would prove nothing");

        failures.Should().BeEmpty(
            "the allocated parts must sum to the discount exactly, with no penny drift, for "
            + "every bill shape (docs/00_ENGINEERING_GUIDE.md §4.1); seed {0}",
            Seed);
    }

    [Fact]
    public void FR_10_2_AllocationSumsExactlyAtTheCurrencyMinorUnitToo()
    {
        var sample = new DeterministicSample(Seed + 1);
        var rounding = new HalfAwayFromZeroRounding(2);
        var failures = new List<string>();

        for (var iteration = 0; iteration < LineSetSamples; iteration++)
        {
            var lineCount = sample.NextInt(1, 51);
            var lineTotals = new Money[lineCount];
            var lineSumScaled = 0L;

            for (var i = 0; i < lineCount; i++)
            {
                // Whole cents: line totals reaching the allocator have already been rounded
                // at the line-total rounding point.
                var scaled = sample.NextScaled(1L, 1_000_001L) * 100L;
                lineTotals[i] = Money.FromScaled(scaled);
                lineSumScaled += scaled;
            }

            var discount = Money.FromScaled(sample.NextScaled(0L, (lineSumScaled / 100L) + 1L) * 100L);

            var allocations = DiscountAllocator.Allocate(discount, lineTotals, rounding);
            var allocated = allocations.Aggregate(Money.Zero, (running, part) => running + part);

            var everyPartIsAWholeCent = allocations.All(
                part => part.Amount == decimal.Round(part.Amount, 2, MidpointRounding.AwayFromZero));

            if (allocated != discount || !everyPartIsAWholeCent)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {iteration}: {lineCount} lines, discount {discount}, "
                    + $"allocated {allocated}"));
            }
        }

        failures.Should().BeEmpty(
            "at the currency's granularity every part must be an amount the shop could hand "
            + "over, and they must still sum exactly; seed {0}",
            Seed + 1);
    }

    [Fact]
    public void FR_10_2_ASingleLineTakesTheWholeDiscount()
    {
        var allocations = DiscountAllocator.Allocate(
            Money.FromDecimal(5m),
            [Money.FromDecimal(20m)]);

        allocations.Should().ContainSingle().Which.Should().Be(Money.FromDecimal(5m));
    }

    [Fact]
    public void FR_10_2_AllocatesProportionallyWhenTheSplitIsExact()
    {
        var allocations = DiscountAllocator.Allocate(
            Money.FromDecimal(10m),
            [Money.FromDecimal(60m), Money.FromDecimal(30m), Money.FromDecimal(10m)],
            decimalPlaces: 2);

        allocations.Should().Equal(
            Money.FromDecimal(6m),
            Money.FromDecimal(3m),
            Money.FromDecimal(1m));
    }

    [Fact]
    public void FR_10_2_EqualLinesSplitEvenlyAndTheResidualUnitGoesToOneLineOnly()
    {
        // 1.00 across three equal lines at two places: 0.3333 each leaves one cent over.
        var allocations = DiscountAllocator.Allocate(
            Money.FromDecimal(1m),
            [Money.FromDecimal(10m), Money.FromDecimal(10m), Money.FromDecimal(10m)],
            decimalPlaces: 2);

        allocations.Should().Equal(
            Money.FromDecimal(0.34m),
            Money.FromDecimal(0.33m),
            Money.FromDecimal(0.33m));

        Sum(allocations).Should().Be(Money.FromDecimal(1m));
    }

    [Fact]
    public void FR_10_2_EqualLinesSplitEvenlyAtTheStorageGranularity()
    {
        var allocations = DiscountAllocator.Allocate(
            Money.FromDecimal(1m),
            [Money.FromDecimal(10m), Money.FromDecimal(10m), Money.FromDecimal(10m)]);

        allocations.Should().Equal(
            Money.FromDecimal(0.3334m),
            Money.FromDecimal(0.3333m),
            Money.FromDecimal(0.3333m));

        Sum(allocations).Should().Be(Money.FromDecimal(1m));
    }

    [Fact]
    public void FR_10_2_TheResidualGoesToTheLargestLineWhenRemaindersTie()
    {
        // 0.10 over 3.00 and 1.00 at two places gives 0.075 and 0.025: both remainders are
        // exactly a half, so the tie is broken by line size.
        var allocations = DiscountAllocator.Allocate(
            Money.FromDecimal(0.10m),
            [Money.FromDecimal(3m), Money.FromDecimal(1m)],
            decimalPlaces: 2);

        allocations.Should().Equal(Money.FromDecimal(0.08m), Money.FromDecimal(0.02m));

        // Same bill, lines the other way round: the residual follows the large line, not the
        // position.
        var reversed = DiscountAllocator.Allocate(
            Money.FromDecimal(0.10m),
            [Money.FromDecimal(1m), Money.FromDecimal(3m)],
            decimalPlaces: 2);

        reversed.Should().Equal(Money.FromDecimal(0.02m), Money.FromDecimal(0.08m));
    }

    [Fact]
    public void FR_10_2_AwkwardSplitAcrossSevenLinesStillSumsExactly()
    {
        var lineTotals = Enumerable.Range(0, 7)
            .Select(_ => Money.FromDecimal(1.43m))
            .ToArray();

        var allocations = DiscountAllocator.Allocate(
            Money.FromDecimal(1m),
            lineTotals,
            decimalPlaces: 2);

        Sum(allocations).Should().Be(Money.FromDecimal(1m));
        allocations.Should().OnlyContain(part => part.Amount == 0.14m || part.Amount == 0.15m);
    }

    [Fact]
    public void FR_10_2_AZeroDiscountAllocatesNothingToEveryLine()
    {
        var allocations = DiscountAllocator.Allocate(
            Money.Zero,
            [Money.FromDecimal(10m), Money.FromDecimal(0m), Money.FromDecimal(5m)]);

        allocations.Should().Equal(Money.Zero, Money.Zero, Money.Zero);
    }

    [Fact]
    public void FR_10_2_AZeroValueLineTakesNoPartOfTheDiscount()
    {
        var allocations = DiscountAllocator.Allocate(
            Money.FromDecimal(1m),
            [Money.FromDecimal(10m), Money.Zero, Money.FromDecimal(10m)],
            decimalPlaces: 2);

        allocations[1].Should().Be(Money.Zero, "a line worth nothing cannot carry a discount");
        Sum(allocations).Should().Be(Money.FromDecimal(1m));
    }

    [Fact]
    public void FR_10_2_FiftyLinesIsSupported()
    {
        var lineTotals = Enumerable.Range(1, 50)
            .Select(i => Money.FromDecimal(i * 1.07m))
            .ToArray();

        var allocations = DiscountAllocator.Allocate(
            Money.FromDecimal(99.99m),
            lineTotals,
            decimalPlaces: 2);

        allocations.Should().HaveCount(50);
        Sum(allocations).Should().Be(Money.FromDecimal(99.99m));
    }

    [Fact]
    public void FR_10_2_RejectsInputThatCannotBeAllocated()
    {
        Action noLines = () => _ = DiscountAllocator.Allocate(Money.FromDecimal(1m), []);
        Action nullLines = () => _ = DiscountAllocator.Allocate(Money.FromDecimal(1m), null!);
        Action negativeDiscount = () => _ = DiscountAllocator.Allocate(
            Money.FromDecimal(-1m), [Money.FromDecimal(10m)]);
        Action negativeLine = () => _ = DiscountAllocator.Allocate(
            Money.FromDecimal(1m), [Money.FromDecimal(10m), Money.FromDecimal(-1m)]);
        Action nothingToBeProportionalTo = () => _ = DiscountAllocator.Allocate(
            Money.FromDecimal(1m), [Money.Zero, Money.Zero]);
        Action tooManyPlaces = () => _ = DiscountAllocator.Allocate(
            Money.FromDecimal(1m), [Money.FromDecimal(10m)], decimalPlaces: 5);
        Action nullPolicy = () => _ = DiscountAllocator.Allocate(
            Money.FromDecimal(1m), [Money.FromDecimal(10m)], (IRoundingPolicy)null!);

        noLines.Should().Throw<ArgumentException>();
        nullLines.Should().Throw<ArgumentNullException>();
        negativeDiscount.Should().Throw<ArgumentOutOfRangeException>();
        negativeLine.Should().Throw<ArgumentException>();
        nothingToBeProportionalTo.Should().Throw<ArgumentException>();
        tooManyPlaces.Should().Throw<ArgumentOutOfRangeException>();
        nullPolicy.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// How many storage units the proportional split leaves over, worked out independently of
    /// the allocator in exact 64-bit integer arithmetic. Used only to prove the random sample
    /// actually reaches the largest-remainder branch.
    /// </summary>
    private static long ResidualUnits(long discountScaled, long[] lineScaled, long lineSumScaled)
    {
        var floored = 0L;

        foreach (var line in lineScaled)
        {
            floored += discountScaled * line / lineSumScaled;
        }

        return discountScaled - floored;
    }

    private static Money Sum(IReadOnlyList<Money> allocations) =>
        allocations.Aggregate(Money.Zero, (running, part) => running + part);
}
