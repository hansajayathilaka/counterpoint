using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Domain.Tests.Support;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.ValueObjects;

/// <summary>
/// SRS DM-01: all monetary values are fixed-precision decimal, never floating point,
/// stored as a 64-bit integer scaled by 10 000 (docs/01_DATA_MODEL.md §1).
/// </summary>
public sealed class MoneyTests
{
    /// <summary>Sample size fixed by the P0-T02 "Done when" list.</summary>
    private const int RoundTripSamples = 10_000;

    /// <summary>Change this only deliberately: it is what makes a red build reproducible.</summary>
    private const int Seed = 20_260_904;

    [Fact]
    public void DM_01_RoundTripsThroughTheScaledIntegerForTenThousandRandomAmounts()
    {
        var sample = new DeterministicSample(Seed);
        var failures = new List<string>();
        var checkedAmounts = 0;
        var negatives = 0;

        for (var i = 0; i < RoundTripSamples; i++)
        {
            var value = sample.NextStorableDecimal();
            var money = Money.FromDecimal(value);

            var scaled = money.ToScaled();
            var roundTripped = Money.FromScaled(scaled);

            checkedAmounts++;
            if (money.IsNegative)
            {
                negatives++;
            }

            if (roundTripped != money || roundTripped.Amount != value)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {i}: {value} -> {scaled} -> {roundTripped.Amount}"));
            }
        }

        checkedAmounts.Should().Be(RoundTripSamples, "the Done-when list fixes the sample size");
        negatives.Should().BeInRange(1, RoundTripSamples - 1,
            "the sample must span both signs, not just credits or just debits");

        failures.Should().BeEmpty(
            "every amount the database can hold must survive Money.FromScaled(x.ToScaled()) "
            + "unchanged (SRS DM-01); seed {0}",
            Seed);
    }

    [Fact]
    public void DM_01_RoundTripsToFourDecimalPlacesWhenGivenMorePreciseInput()
    {
        var sample = new DeterministicSample(Seed + 1);
        var failures = new List<string>();

        for (var i = 0; i < RoundTripSamples; i++)
        {
            var value = sample.NextOverPreciseDecimal();
            var expected = decimal.Round(value, 4, MidpointRounding.AwayFromZero);

            var roundTripped = Money.FromScaled(Money.FromDecimal(value).ToScaled());

            if (roundTripped.Amount != expected)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {i}: {value} -> {roundTripped.Amount}, expected {expected}"));
            }
        }

        failures.Should().BeEmpty(
            "an amount carrying more precision than the storage scale is quantised to four "
            + "decimal places, half away from zero, and nothing else moves; seed {0}",
            Seed + 1);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(10_000L)]
    [InlineData(-10_000L)]
    [InlineData(12_345_678L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void DM_01_FromScaledIsTheExactInverseOfToScaled(long scaled)
    {
        Money.FromScaled(scaled).ToScaled().Should().Be(scaled);
    }

    [Fact]
    public void DM_01_ScaledIntegerFollowsTheDataModelConvention()
    {
        // docs/01_DATA_MODEL.md §1: "12345678 = 1 234.5678".
        Money.FromScaled(12_345_678L).Amount.Should().Be(1_234.5678m);
        Money.FromDecimal(1_234.5678m).ToScaled().Should().Be(12_345_678L);
        Money.MoneyScale.Should().Be(10_000L);
        Money.MoneyDecimalPlaces.Should().Be(4);
    }

    [Fact]
    public void DM_01_ToScaledThrowsRatherThanWrappingAtThePositiveBoundary()
    {
        // long.MaxValue / 10 000 = 922 337 203 685 477.5807 - the last storable amount.
        Money.MaxValue.Amount.Should().Be(922_337_203_685_477.5807m);
        Money.MaxValue.ToScaled().Should().Be(long.MaxValue);

        Action oneUnitOver = () => _ = Money.FromDecimal(922_337_203_685_477.5808m).ToScaled();
        Action roundsOver = () => _ = Money.FromDecimal(922_337_203_685_477.58075m).ToScaled();
        Action farOver = () => _ = Money.FromDecimal(decimal.MaxValue).ToScaled();

        oneUnitOver.Should().Throw<OverflowException>(
            "a wrapped total is a silently wrong bill; it must throw instead");
        roundsOver.Should().Throw<OverflowException>(
            "quantising half away from zero pushes this past the boundary");
        farOver.Should().Throw<OverflowException>();
    }

    [Fact]
    public void DM_01_ToScaledThrowsRatherThanWrappingAtTheNegativeBoundary()
    {
        // long.MinValue / 10 000 = -922 337 203 685 477.5808.
        Money.MinValue.Amount.Should().Be(-922_337_203_685_477.5808m);
        Money.MinValue.ToScaled().Should().Be(long.MinValue);

        Action oneUnitUnder = () => _ = Money.FromDecimal(-922_337_203_685_477.5809m).ToScaled();
        Action roundsUnder = () => _ = Money.FromDecimal(-922_337_203_685_477.58085m).ToScaled();
        Action farUnder = () => _ = Money.FromDecimal(decimal.MinValue).ToScaled();

        oneUnitUnder.Should().Throw<OverflowException>();
        roundsUnder.Should().Throw<OverflowException>();
        farUnder.Should().Throw<OverflowException>();
    }

    [Theory]
    [InlineData("0.00005", 1L)]
    [InlineData("-0.00005", -1L)]
    [InlineData("0.00004", 0L)]
    [InlineData("-0.00004", 0L)]
    [InlineData("1.23455", 12_346L)]
    [InlineData("-1.23455", -12_346L)]
    public void DM_01_ToScaledQuantisesHalfAwayFromZero(string amount, long expected)
    {
        var money = Money.FromDecimal(decimal.Parse(amount, CultureInfo.InvariantCulture));

        money.ToScaled().Should().Be(expected);
    }

    [Fact]
    public void DM_01_AddsAndSubtractsExactly()
    {
        var a = Money.FromDecimal(19.99m);
        var b = Money.FromDecimal(0.01m);

        (a + b).Should().Be(Money.FromDecimal(20.00m));
        (a - b).Should().Be(Money.FromDecimal(19.98m));
        a.Add(b).Should().Be(a + b);
        a.Subtract(b).Should().Be(a - b);
    }

    [Fact]
    public void DM_01_MultipliesAndDividesByADecimalWithoutBinaryDrift()
    {
        var price = Money.FromDecimal(0.10m);

        // The canonical binary floating point failure: 0.1 * 3 is not 0.3 in double.
        (price * 3m).Should().Be(Money.FromDecimal(0.30m));
        price.Multiply(3m).Should().Be(price * 3m);
        (3m * price).Should().Be(price * 3m);

        (Money.FromDecimal(10m) / 4m).Should().Be(Money.FromDecimal(2.5m));
        Money.FromDecimal(10m).Divide(4m).Should().Be(Money.FromDecimal(2.5m));
    }

    [Fact]
    public void DM_01_DividingByZeroThrows()
    {
        Action act = () => _ = Money.FromDecimal(10m) / 0m;

        act.Should().Throw<DivideByZeroException>();
    }

    [Fact]
    public void DM_01_ComparesAmounts()
    {
        var small = Money.FromDecimal(1.0000m);
        var large = Money.FromDecimal(1.0001m);
        var alsoSmall = Money.FromDecimal(1.0000m);

        (small < large).Should().BeTrue();
        (large > small).Should().BeTrue();
        (small <= large).Should().BeTrue();
        (large >= small).Should().BeTrue();
        (small <= alsoSmall).Should().BeTrue();
        (small >= alsoSmall).Should().BeTrue();
        (small < alsoSmall).Should().BeFalse();
        (small > alsoSmall).Should().BeFalse();
        small.CompareTo(large).Should().BeNegative();
        large.CompareTo(small).Should().BePositive();
        small.CompareTo(alsoSmall).Should().Be(0);
    }

    [Fact]
    public void DM_01_EqualityIgnoresTrailingZeroesInTheDecimal()
    {
        var written = Money.FromDecimal(1.5m);
        var stored = Money.FromScaled(15_000L);

        (written == stored).Should().BeTrue();
        (written != stored).Should().BeFalse();
        written.GetHashCode().Should().Be(stored.GetHashCode(),
            "1.5 and 1.5000 are the same amount, so they must hash the same");
    }

    [Fact]
    public void DM_01_ExposesSignHelpersAndZero()
    {
        Money.Zero.Amount.Should().Be(0m);
        Money.Zero.IsZero.Should().BeTrue();
        default(Money).Should().Be(Money.Zero, "an unset Money is nothing owed, not nonsense");

        var negative = Money.FromDecimal(-2.5m);
        negative.IsNegative.Should().BeTrue();
        negative.IsPositive.Should().BeFalse();
        negative.Abs().Should().Be(Money.FromDecimal(2.5m));
        negative.Negate().Should().Be(Money.FromDecimal(2.5m));
        (-negative).Should().Be(Money.FromDecimal(2.5m));

        var positive = Money.FromDecimal(2.5m);
        positive.IsPositive.Should().BeTrue();
        positive.IsNegative.Should().BeFalse();
        positive.Abs().Should().Be(positive);
        positive.Negate().Should().Be(negative);
    }

    [Fact]
    public void DM_01_FormatsCultureInvariantly()
    {
        Money.FromDecimal(1_234.5678m).ToString().Should().Be("1234.5678");
    }
}
