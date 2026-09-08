using System;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.ValueObjects;

/// <summary>
/// <c>product_uom.conversion_factor</c>: how many base units one of some other unit is worth
/// (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.4).
/// </summary>
public sealed class UomConversionTests
{
    [Fact]
    public void FR_2_4_BaseIsExactlyOne()
    {
        UomConversion.Base.Factor.Should().Be(1m);
        UomConversion.Base.IsBase.Should().BeTrue();
    }

    [Fact]
    public void FR_2_4_ABoxOfOneHundredPiecesRoundTripsThroughStorageExactly()
    {
        var factor = UomConversion.FromDecimal(100m);

        factor.ToScaled().Should().Be(1_000_000L, "1 box = 100 pieces is 1000000 (docs/01_DATA_MODEL.md §3)");
        UomConversion.FromScaled(factor.ToScaled()).Factor.Should().Be(100m);
    }

    [Fact]
    public void FR_2_4_RejectsAZeroOrNegativeFactor()
    {
        var zero = () => UomConversion.FromDecimal(0m);
        var negative = () => UomConversion.FromDecimal(-1m);
        var scaledZero = () => UomConversion.FromScaled(0L);

        zero.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
        scaledZero.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FR_2_4_OnlyAFactorOfExactlyOneIsBase()
    {
        UomConversion.FromDecimal(1m).IsBase.Should().BeTrue();
        UomConversion.FromDecimal(0.9999m).IsBase.Should().BeFalse();
        UomConversion.FromDecimal(90m).IsBase.Should().BeFalse();
    }

    [Fact]
    public void FR_2_4_OrdersByFactor()
    {
        var small = UomConversion.FromDecimal(1m);
        var large = UomConversion.FromDecimal(100m);

        (small < large).Should().BeTrue();
        (large > small).Should().BeTrue();
        (small <= large).Should().BeTrue();
        (large >= small).Should().BeTrue();
    }
}
