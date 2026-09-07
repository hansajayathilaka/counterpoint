using System;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Services;

/// <summary>
/// The second rounding rule the shop may choose (SRS FR-10.2). Its existence is what makes
/// "rounding rule" a setting rather than a comment.
/// </summary>
public sealed class HalfToEvenRoundingTests
{
    [Theory]
    [InlineData("0.125", "0.12")]
    [InlineData("0.135", "0.14")]
    [InlineData("2.675", "2.68")]
    [InlineData("-0.125", "-0.12")]
    [InlineData("-0.135", "-0.14")]
    public void FR_10_2_AMidpointGoesToTheEvenDigit(string amount, string expected)
    {
        var rounded = new HalfToEvenRounding(decimalPlaces: 2)
            .Round(Money.FromDecimal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

        rounded.Amount.Should().Be(
            decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            "banker's rounding sends a midpoint to the nearer even digit");
    }

    [Fact]
    public void FR_10_2_ItDiffersFromTheDefaultRuleExactlyAtAMidpoint()
    {
        var midpoint = Money.FromDecimal(0.125m);

        new HalfAwayFromZeroRounding(2).Round(midpoint).Amount.Should().Be(0.13m);
        new HalfToEvenRounding(2).Round(midpoint).Amount.Should().Be(
            0.12m,
            "if the two rules agreed everywhere, the setting would be decorative");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void FR_10_2_ItRefusesMoreDecimalPlacesThanTheStorageScaleCarries(int places)
    {
        var construct = () => new HalfToEvenRounding(places);

        construct.Should().Throw<ArgumentOutOfRangeException>(
            "money is stored scaled by 10 000, so only 0 to 4 places exist (CLAUDE.md invariant 1)");
    }

    [Theory]
    [InlineData(RoundingRule.HalfAwayFromZero, typeof(HalfAwayFromZeroRounding))]
    [InlineData(RoundingRule.HalfToEven, typeof(HalfToEvenRounding))]
    public void FR_10_2_TheFactoryBuildsThePolicyTheSettingNames(RoundingRule rule, Type expected)
    {
        var policy = RoundingPolicyFactory.Create(rule, decimalPlaces: 3);

        policy.Should().BeOfType(expected);
        policy.DecimalPlaces.Should().Be(3, "the decimal places come from the setting too");
    }
}
