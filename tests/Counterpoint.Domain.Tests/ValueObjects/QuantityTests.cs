using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Domain.Tests.Support;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.ValueObjects;

/// <summary>
/// SRS DM-02: quantities are decimal with the configured precision, stored scaled by 10 000
/// and always carrying the unit of measure they were counted in.
/// </summary>
public sealed class QuantityTests
{
    private const int RoundTripSamples = 10_000;
    private const int Seed = 20_260_905;

    private const long Metres = 1L;
    private const long Pieces = 2L;

    [Fact]
    public void DM_02_RoundTripsThroughTheScaledIntegerForTenThousandRandomQuantities()
    {
        var sample = new DeterministicSample(Seed);
        var failures = new List<string>();

        for (var i = 0; i < RoundTripSamples; i++)
        {
            var value = sample.NextStorableDecimal();
            var quantity = Quantity.FromDecimal(value, Metres);

            var scaled = quantity.ToScaled();
            var roundTripped = Quantity.FromScaled(scaled, Metres);

            if (roundTripped != quantity || roundTripped.Value != value)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {i}: {value} -> {scaled} -> {roundTripped.Value}"));
            }
        }

        failures.Should().BeEmpty(
            "a quantity written to the ledger and read back must be the same quantity "
            + "(SRS DM-02); seed {0}",
            Seed);
    }

    [Fact]
    public void DM_02_ScaledIntegerFollowsTheDataModelConvention()
    {
        Quantity.FromScaled(12_345_678L, Metres).Value.Should().Be(1_234.5678m);
        Quantity.FromDecimal(1_234.5678m, Metres).ToScaled().Should().Be(12_345_678L);
        Quantity.QtyScale.Should().Be(10_000L);
        Quantity.QtyDecimalPlaces.Should().Be(4);
    }

    [Fact]
    public void DM_02_KeepsTheUnitItWasCountedIn()
    {
        var quantity = Quantity.FromDecimal(2.5m, Metres);

        quantity.UomId.Should().Be(Metres);
        quantity.Negate().UomId.Should().Be(Metres);
        quantity.Abs().UomId.Should().Be(Metres);
        (quantity * 3m).UomId.Should().Be(Metres);
        (quantity / 2m).UomId.Should().Be(Metres);
        Quantity.Zero(Pieces).UomId.Should().Be(Pieces);
    }

    [Fact]
    public void DM_02_AddsAndSubtractsWithinOneUnitOfMeasure()
    {
        var twoMetres = Quantity.FromDecimal(2m, Metres);
        var halfMetre = Quantity.FromDecimal(0.5m, Metres);

        (twoMetres + halfMetre).Should().Be(Quantity.FromDecimal(2.5m, Metres));
        (twoMetres - halfMetre).Should().Be(Quantity.FromDecimal(1.5m, Metres));
        twoMetres.Add(halfMetre).Should().Be(twoMetres + halfMetre);
        twoMetres.Subtract(halfMetre).Should().Be(twoMetres - halfMetre);
        twoMetres.Multiply(3m).Should().Be(Quantity.FromDecimal(6m, Metres));
        twoMetres.Divide(4m).Should().Be(Quantity.FromDecimal(0.5m, Metres));
    }

    [Fact]
    public void DM_02_ArithmeticAcrossDifferentUnitsOfMeasureThrows()
    {
        var metres = Quantity.FromDecimal(2m, Metres);
        var pieces = Quantity.FromDecimal(2m, Pieces);

        Action add = () => _ = metres + pieces;
        Action subtract = () => _ = metres - pieces;

        add.Should().Throw<InvalidOperationException>(
                "adding metres to pieces is a programming error, not something a cashier can do")
            .WithMessage("*different units of measure*");
        subtract.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DM_02_ComparisonAcrossDifferentUnitsOfMeasureThrows()
    {
        var metres = Quantity.FromDecimal(2m, Metres);
        var pieces = Quantity.FromDecimal(3m, Pieces);

        Action lessThan = () => _ = metres < pieces;
        Action greaterThan = () => _ = metres > pieces;
        Action lessOrEqual = () => _ = metres <= pieces;
        Action greaterOrEqual = () => _ = metres >= pieces;
        Action compareTo = () => _ = metres.CompareTo(pieces);

        lessThan.Should().Throw<InvalidOperationException>();
        greaterThan.Should().Throw<InvalidOperationException>();
        lessOrEqual.Should().Throw<InvalidOperationException>();
        greaterOrEqual.Should().Throw<InvalidOperationException>();
        compareTo.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void DM_02_EqualityAcrossDifferentUnitsOfMeasureIsFalseRatherThanAnError()
    {
        var metres = Quantity.FromDecimal(2m, Metres);
        var pieces = Quantity.FromDecimal(2m, Pieces);

        (metres == pieces).Should().BeFalse("two metres is not two pieces");
        (metres != pieces).Should().BeTrue();
    }

    [Fact]
    public void DM_02_ComparesWithinOneUnitOfMeasure()
    {
        var small = Quantity.FromDecimal(2m, Metres);
        var large = Quantity.FromDecimal(2.0001m, Metres);
        var alsoSmall = Quantity.FromDecimal(2m, Metres);

        (small < large).Should().BeTrue();
        (large > small).Should().BeTrue();
        (small <= alsoSmall).Should().BeTrue();
        (small >= alsoSmall).Should().BeTrue();
        small.CompareTo(large).Should().BeNegative();
        small.CompareTo(alsoSmall).Should().Be(0);
    }

    [Fact]
    public void DM_02_ToScaledThrowsRatherThanWrappingAtBothBoundaries()
    {
        Quantity.FromScaled(long.MaxValue, Metres).ToScaled().Should().Be(long.MaxValue);
        Quantity.FromScaled(long.MinValue, Metres).ToScaled().Should().Be(long.MinValue);

        Action over = () => _ = Quantity.FromDecimal(922_337_203_685_477.5808m, Metres).ToScaled();
        Action under = () => _ = Quantity.FromDecimal(-922_337_203_685_477.5809m, Metres).ToScaled();

        over.Should().Throw<OverflowException>();
        under.Should().Throw<OverflowException>();
    }

    [Fact]
    public void DM_02_ExposesSignHelpersAndZero()
    {
        var issue = Quantity.FromDecimal(-3m, Metres);

        issue.IsNegative.Should().BeTrue();
        issue.IsPositive.Should().BeFalse();
        issue.Abs().Should().Be(Quantity.FromDecimal(3m, Metres));
        (-issue).Should().Be(Quantity.FromDecimal(3m, Metres));
        Quantity.Zero(Metres).IsZero.Should().BeTrue();
    }
}
