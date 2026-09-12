using System;
using Counterpoint.Domain.Purchasing;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Purchasing;

/// <summary>
/// <see cref="GoodsReceiptFreightApportioner"/> - the pure freight-apportionment rule
/// <c>Counterpoint.Application.Purchasing.GoodsReceiptService.ReceiveAsync</c> applies (SRS
/// FR-4.7, FR-4.8, task P2-T07 "Do this" #3). The "sums to exactly the entered freight" property
/// is this class's whole reason to exist, so most of these tests prove exactly that, over inputs
/// chosen to not divide evenly.
/// </summary>
public sealed class GoodsReceiptFreightApportionerTests
{
    [Fact]
    public void FR_4_7_SharesAreProportionalToEachLinesOwnSubtotal()
    {
        // 300 split 100:200 (1:2) should land 100 and 200 exactly - no remainder to distribute.
        var shares = GoodsReceiptFreightApportioner.Apportion(
            Money.FromDecimal(300.00m),
            [Money.FromDecimal(1000.00m), Money.FromDecimal(2000.00m)]);

        shares.Should().Equal(Money.FromDecimal(100.00m), Money.FromDecimal(200.00m));
    }

    [Fact]
    public void FR_4_7_SharesSumToExactlyTheEnteredFreightEvenWhenTheSplitDoesNotDivideEvenly()
    {
        // Rs 100 split three ways by equal subtotals: 33.3333... each. Naive independent
        // rounding could land on 33.33 x 3 = 99.99, a cent short of what was entered.
        var otherCost = Money.FromDecimal(100.00m);

        var shares = GoodsReceiptFreightApportioner.Apportion(
            otherCost,
            [Money.FromDecimal(500.00m), Money.FromDecimal(500.00m), Money.FromDecimal(500.00m)]);

        shares.Should().HaveCount(3);
        Money.FromScaled(shares[0].ToScaled() + shares[1].ToScaled() + shares[2].ToScaled())
            .Should().Be(otherCost, "the three shares must sum to exactly the freight entered, to the ten-thousandth");
    }

    [Fact]
    public void FR_4_7_SharesSumExactlyAcrossManyUnevenLines()
    {
        // Seven lines, each subtotal one currency unit apart, freight not a multiple of seven -
        // the largest-remainder distribution has to place every leftover scaled unit somewhere.
        var subtotals = new[]
        {
            Money.FromDecimal(101.00m), Money.FromDecimal(202.00m), Money.FromDecimal(303.00m),
            Money.FromDecimal(404.00m), Money.FromDecimal(505.00m), Money.FromDecimal(606.00m),
            Money.FromDecimal(707.00m),
        };
        var otherCost = Money.FromDecimal(1000.01m);

        var shares = GoodsReceiptFreightApportioner.Apportion(otherCost, subtotals);

        var sum = 0L;
        foreach (var share in shares)
        {
            sum += share.ToScaled();
        }

        Money.FromScaled(sum).Should().Be(otherCost);
    }

    [Fact]
    public void FR_4_7_ZeroFreightApportionsNothingToAnyLine()
    {
        var shares = GoodsReceiptFreightApportioner.Apportion(
            Money.Zero,
            [Money.FromDecimal(100.00m), Money.FromDecimal(200.00m)]);

        shares.Should().Equal(Money.Zero, Money.Zero);
    }

    [Fact]
    public void FR_4_7_EveryLineCostingNothingSplitsFreightEvenlyByCount()
    {
        var shares = GoodsReceiptFreightApportioner.Apportion(
            Money.FromDecimal(10.00m),
            [Money.Zero, Money.Zero]);

        shares.Should().Equal(Money.FromDecimal(5.00m), Money.FromDecimal(5.00m));
    }

    [Fact]
    public void FR_4_7_ASingleLineReceivesTheWholeFreight()
    {
        var otherCost = Money.FromDecimal(37.77m);

        var shares = GoodsReceiptFreightApportioner.Apportion(otherCost, [Money.FromDecimal(999.00m)]);

        shares.Should().Equal(otherCost);
    }

    [Fact]
    public void FR_4_7_NegativeFreightIsRefused()
    {
        var act = () => GoodsReceiptFreightApportioner.Apportion(
            Money.FromDecimal(-1.00m),
            [Money.FromDecimal(100.00m)]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void FR_4_7_NoLinesToApportionAcrossIsRefused()
    {
        var act = () => GoodsReceiptFreightApportioner.Apportion(Money.FromDecimal(10.00m), []);

        act.Should().Throw<ArgumentException>();
    }
}
