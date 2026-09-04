using System;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.ValueObjects;

/// <summary>
/// docs/01_DATA_MODEL.md §1: a rate is an INTEGER scaled ×10 000 over the fraction —
/// <c>1500</c> is 15.00%, i.e. 0.1500.
/// </summary>
public sealed class PercentageTests
{
    [Fact]
    public void DM_01_FifteenHundredScaledIsFifteenPercent()
    {
        var rate = Percentage.FromScaled(1_500L);

        rate.Fraction.Should().Be(0.15m);
        rate.AsPercent.Should().Be(15m);
        rate.ToScaled().Should().Be(1_500L);
        Percentage.RateScale.Should().Be(10_000L);
    }

    [Fact]
    public void DM_01_PercentAndFractionAgree()
    {
        Percentage.FromPercent(15m).Should().Be(Percentage.FromFraction(0.15m));
        Percentage.FromPercent(2.5m).ToScaled().Should().Be(250L);
        Percentage.FromPercent(100m).Should().Be(Percentage.OneHundredPercent);
        Percentage.Zero.IsZero.Should().BeTrue();
        Percentage.FromPercent(0.01m).ToScaled().Should().Be(1L,
            "one hundredth of a percent is the finest rate the scale can hold");
    }

    [Fact]
    public void DM_01_RoundTripsThroughTheScaledIntegerAtEveryStorableRate()
    {
        // 0.00% to 100.00% in hundredths of a percent - the whole space a rate column uses.
        for (var scaled = 0L; scaled <= 10_000L; scaled++)
        {
            Percentage.FromScaled(scaled).ToScaled().Should().Be(scaled);
        }
    }

    [Fact]
    public void FR_10_2_TakesAProportionOfAnAmountWithoutRounding()
    {
        var tenPercent = Percentage.FromPercent(10m);

        // 33.33 is not divisible by ten: the unrounded answer keeps the third decimal, because
        // rounding belongs to the line total and the bill total, not to this call.
        tenPercent.Of(Money.FromDecimal(33.33m)).Amount.Should().Be(3.333m);
        tenPercent.RemainderOf(Money.FromDecimal(33.33m)).Amount.Should().Be(29.997m);
        tenPercent.Of(Money.FromDecimal(200m)).Should().Be(Money.FromDecimal(20m));
    }

    [Fact]
    public void DM_01_ComparesRates()
    {
        var five = Percentage.FromPercent(5m);
        var ten = Percentage.FromPercent(10m);
        var alsoFive = Percentage.FromPercent(5m);

        (five < ten).Should().BeTrue();
        (ten > five).Should().BeTrue();
        (five <= alsoFive).Should().BeTrue();
        (five >= alsoFive).Should().BeTrue();
        five.CompareTo(ten).Should().BeNegative();
        five.IsPositive.Should().BeTrue();
    }

    [Fact]
    public void DM_01_ToScaledThrowsRatherThanWrapping()
    {
        Action act = () => _ = Percentage.FromFraction(decimal.MaxValue).ToScaled();

        act.Should().Throw<OverflowException>();
    }

    [Fact]
    public void DM_01_FormatsCultureInvariantly()
    {
        Percentage.FromPercent(15m).ToString().Should().Be("15%");
    }
}
