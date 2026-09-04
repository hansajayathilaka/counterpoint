using System;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.ValueObjects;

/// <summary>
/// <c>tax_class.rate</c> is an INTEGER scaled ×10 000 over the fraction (docs/01_DATA_MODEL.md).
/// The two ways of applying it — on top of a net price, or carved out of a tax-inclusive
/// price — must agree, and neither may round (CLAUDE.md invariant 2).
/// </summary>
public sealed class TaxRateTests
{
    [Fact]
    public void DM_01_FifteenHundredScaledIsFifteenPercent()
    {
        var rate = TaxRate.FromScaled(1_500L);

        rate.Rate.Should().Be(0.15m);
        rate.AsPercent.Should().Be(15m);
        rate.ToScaled().Should().Be(1_500L);
        rate.ToString().Should().Be("15%");
        TaxRate.RateScale.Should().Be(10_000L);
    }

    [Fact]
    public void DM_01_ZeroRatedIsTheDefault()
    {
        TaxRate.Zero.IsZero.Should().BeTrue();
        TaxRate.Zero.ToScaled().Should().Be(0L);
        TaxRate.Zero.TaxOnNet(Money.FromDecimal(100m)).Should().Be(Money.Zero);
        TaxRate.Zero.TaxWithinGross(Money.FromDecimal(100m)).Should().Be(Money.Zero);
    }

    [Fact]
    public void DM_01_ANegativeTaxRateIsRejected()
    {
        Action fromFraction = () => _ = TaxRate.FromFraction(-0.01m);
        Action fromPercent = () => _ = TaxRate.FromPercent(-1m);
        Action fromScaled = () => _ = TaxRate.FromScaled(-1L);

        fromFraction.Should().Throw<ArgumentOutOfRangeException>();
        fromPercent.Should().Throw<ArgumentOutOfRangeException>();
        fromScaled.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FR_10_2_TaxExclusiveAddsTheRateOnTopOfNet()
    {
        var fifteenPercent = TaxRate.FromPercent(15m);
        var net = Money.FromDecimal(100m);

        fifteenPercent.TaxOnNet(net).Should().Be(Money.FromDecimal(15m));
        fifteenPercent.GrossFromNet(net).Should().Be(Money.FromDecimal(115m));
    }

    [Fact]
    public void FR_10_2_TaxInclusiveCarvesTheRateOutOfGross()
    {
        var fifteenPercent = TaxRate.FromPercent(15m);
        var gross = Money.FromDecimal(115m);

        fifteenPercent.TaxWithinGross(gross).Should().Be(Money.FromDecimal(15m));
        fifteenPercent.NetFromGross(gross).Should().Be(Money.FromDecimal(100m));
    }

    [Fact]
    public void FR_10_2_TaxCalculationsDoNotRoundThemselves()
    {
        var fifteenPercent = TaxRate.FromPercent(15m);
        var rounding = new HalfAwayFromZeroRounding(2);

        // 15% of 33.33 is 4.9995. Rounding it here, then multiplying by a quantity, is exactly
        // how a bill drifts by a cent per line; the value object hands back the full precision
        // and the caller rounds once, at the line total.
        var unrounded = fifteenPercent.TaxOnNet(Money.FromDecimal(33.33m));

        unrounded.Amount.Should().Be(4.9995m);
        rounding.Round(unrounded).Should().Be(Money.FromDecimal(5.00m));
    }

    [Fact]
    public void DM_01_ComparesRates()
    {
        var five = TaxRate.FromPercent(5m);
        var fifteen = TaxRate.FromPercent(15m);
        var alsoFive = TaxRate.FromPercent(5m);

        (five < fifteen).Should().BeTrue();
        (fifteen > five).Should().BeTrue();
        (five <= alsoFive).Should().BeTrue();
        (five >= alsoFive).Should().BeTrue();
        five.CompareTo(fifteen).Should().BeNegative();
    }
}
