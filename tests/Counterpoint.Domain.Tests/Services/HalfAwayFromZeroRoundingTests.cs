using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Services;

/// <summary>
/// SRS FR-10.2: decimal places and the rounding rule are settings. The default rule is half
/// away from zero, applied at exactly two points — line total and bill total
/// (CLAUDE.md invariant 2).
/// </summary>
public sealed class HalfAwayFromZeroRoundingTests
{
    [Theory]
    // Zero places: the classic midpoints, both signs.
    [InlineData(0, "0.5", "1")]
    [InlineData(0, "1.5", "2")]
    [InlineData(0, "2.5", "3")]
    [InlineData(0, "-0.5", "-1")]
    [InlineData(0, "-1.5", "-2")]
    [InlineData(0, "-2.5", "-3")]
    // Two places: the currency case (Q-01, LKR at two places).
    [InlineData(2, "0.005", "0.01")]
    [InlineData(2, "0.015", "0.02")]
    [InlineData(2, "1.005", "1.01")]
    [InlineData(2, "-0.005", "-0.01")]
    [InlineData(2, "-0.015", "-0.02")]
    [InlineData(2, "-1.005", "-1.01")]
    // Four places: the storage scale itself.
    [InlineData(4, "0.00005", "0.0001")]
    [InlineData(4, "-0.00005", "-0.0001")]
    [InlineData(4, "1.23455", "1.2346")]
    [InlineData(4, "-1.23455", "-1.2346")]
    public void FR_10_2_RoundsMidpointsAwayFromZeroInBothSigns(
        int decimalPlaces, string amount, string expected)
    {
        var policy = new HalfAwayFromZeroRounding(decimalPlaces);

        var rounded = policy.Round(Money.FromDecimal(Parse(amount)));

        rounded.Should().Be(Money.FromDecimal(Parse(expected)),
            "half away from zero is what a shopkeeper expects, and it keeps a return the "
            + "exact mirror of the sale it reverses");
    }

    [Theory]
    [InlineData(2, "0.004", "0.00")]
    [InlineData(2, "-0.004", "0.00")]
    [InlineData(2, "0.006", "0.01")]
    [InlineData(2, "-0.006", "-0.01")]
    [InlineData(2, "19.99", "19.99")]
    [InlineData(0, "0.4", "0")]
    [InlineData(0, "-0.4", "0")]
    public void FR_10_2_LeavesNonMidpointsToNormalRounding(
        int decimalPlaces, string amount, string expected)
    {
        var policy = new HalfAwayFromZeroRounding(decimalPlaces);

        policy.Round(Money.FromDecimal(Parse(amount)))
            .Should().Be(Money.FromDecimal(Parse(expected)));
    }

    [Fact]
    public void FR_10_2_RoundsInDecimalSoTheBinaryFloatingPointTrapDoesNotApply()
    {
        var policy = new HalfAwayFromZeroRounding(2);

        // 2.675 is the standard demonstration: as binary floating point it is really
        // 2.67499999999999982236431605997495353221893310546875 and rounds down to 2.67.
        // As a decimal it is exactly 2.675 and rounds to 2.68 (CLAUDE.md invariant 1).
        policy.Round(Money.FromDecimal(2.675m)).Should().Be(Money.FromDecimal(2.68m));
        policy.Round(Money.FromDecimal(1.005m)).Should().Be(Money.FromDecimal(1.01m));
        policy.Round(Money.FromDecimal(8.475m)).Should().Be(Money.FromDecimal(8.48m));
    }

    [Fact]
    public void FR_10_2_RoundingIsIdempotent()
    {
        var policy = new HalfAwayFromZeroRounding(2);
        var once = policy.Round(Money.FromDecimal(12.345m));

        policy.Round(once).Should().Be(once,
            "rounding an already-rounded amount must be a no-op, or the two rounding points "
            + "would not be safe to assert against each other");
    }

    [Fact]
    public void FR_10_2_ExposesTheConfiguredDecimalPlaces()
    {
        new HalfAwayFromZeroRounding(0).DecimalPlaces.Should().Be(0);
        new HalfAwayFromZeroRounding(2).DecimalPlaces.Should().Be(2);
        new HalfAwayFromZeroRounding(4).DecimalPlaces.Should().Be(4);
        HalfAwayFromZeroRounding.MaxDecimalPlaces.Should().Be(Money.MoneyDecimalPlaces);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(28)]
    public void FR_10_2_RejectsDecimalPlacesTheStorageScaleCannotHold(int decimalPlaces)
    {
        Action act = () => _ = new HalfAwayFromZeroRounding(decimalPlaces);

        act.Should().Throw<ArgumentOutOfRangeException>(
            "money is stored scaled by 10 000, so anything beyond four places would be a lie");
    }

    [Fact]
    public void FR_10_2_IsUsableThroughTheInterface()
    {
        // The domain only ever sees IRoundingPolicy; the concrete rule comes from settings.
        AssertRoundsThroughTheInterface(new HalfAwayFromZeroRounding(2));
    }

    [SuppressMessage(
        "Performance",
        "CA1859:Use concrete types when possible for improved performance",
        Justification = "Dispatching through IRoundingPolicy is exactly what this test asserts: "
            + "the domain only ever sees the interface, and the rule comes from settings (FR-10.2).")]
    private static void AssertRoundsThroughTheInterface(IRoundingPolicy policy)
    {
        policy.Round(Money.FromDecimal(1.005m)).Should().Be(Money.FromDecimal(1.01m));
        policy.Round(Money.FromDecimal(-1.005m)).Should().Be(Money.FromDecimal(-1.01m));
        policy.DecimalPlaces.Should().Be(2);
    }

    private static decimal Parse(string value) =>
        decimal.Parse(value, CultureInfo.InvariantCulture);
}
