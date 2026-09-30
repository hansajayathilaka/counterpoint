using System;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Reporting.Queries;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The arithmetic every P3-T05 slice shares (<c>CanonicalFigures</c>, docs/report-definitions.md
/// section 2). Pure - no database - so each definition is pinned on its own, including the
/// zero-denominator guards the screens rely on.
/// </summary>
public sealed class CanonicalFiguresTests
{
    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private static long Scaled(decimal amount) => M(amount).ToScaled();

    [Fact]
    public void FR_9_6_GrossIsTheSubtotalPlusTheLineDiscountBecauseSubtotalIsAlreadyNetOfIt()
    {
        CanonicalFigures.Gross(Scaled(520m), Scaled(30m)).Should().Be(M(550m));
        CanonicalFigures.Gross(0, 0).Should().Be(Money.Zero);
    }

    [Fact]
    public void FR_9_6_DiscountsAreTheLinePlusTheBillDiscount()
    {
        CanonicalFigures.Discounts(Scaled(30m), Scaled(26m)).Should().Be(M(56m));
    }

    [Fact]
    public void FR_9_6_NetIsGrossMinusDiscountsMinusReturnsWhichReducesToSubtotalMinusBillDiscountMinusReturns()
    {
        // B1 of the hand-worked dataset less a 85.50 return: 550.00 - 56.00 - 85.50.
        CanonicalFigures.Net(Scaled(520m), Scaled(30m), Scaled(26m), Scaled(85.50m)).Should().Be(M(408.50m));

        // A range of returns and no sales is negative, not clamped.
        CanonicalFigures.Net(0, 0, 0, Scaled(100m)).Should().Be(M(-100m));
    }

    [Fact]
    public void FR_9_6_NetEqualsSubtotalMinusBillDiscountMinusReturnsForAnyFigures()
    {
        var random = new Random(20_260_930);

        for (var i = 0; i < 10_000; i++)
        {
            var subtotal = random.NextInt64(0, 50_000_000_000);
            var lineDiscount = random.NextInt64(0, 5_000_000_000);
            var billDiscount = random.NextInt64(0, 5_000_000_000);
            var returns = random.NextInt64(0, 50_000_000_000);

            CanonicalFigures.Net(subtotal, lineDiscount, billDiscount, returns).ToScaled()
                .Should().Be(subtotal - billDiscount - returns, "the line discount is inside subtotal and must cancel exactly");
            CanonicalFigures.Gross(subtotal, lineDiscount).ToScaled().Should().Be(subtotal + lineDiscount);
            CanonicalFigures.Discounts(lineDiscount, billDiscount).ToScaled().Should().Be(lineDiscount + billDiscount);
        }
    }

    [Fact]
    public void FR_9_4_CogsIsTheSnapshotCostTimesTheBaseQuantityMultipliedInDecimal()
    {
        CanonicalFigures.LineCogs(Scaled(60m), Scaled(3m)).Should().Be(M(180m));
        CanonicalFigures.LineCogs(Scaled(149.9999m), Scaled(3m)).Should().Be(M(449.9997m));
        CanonicalFigures.LineCogs(Scaled(4m), Scaled(24m)).Should().Be(M(96m), "a box of 12 costs per base piece");
        CanonicalFigures.LineCogs(Scaled(60m), 0).Should().Be(Money.Zero);
        CanonicalFigures.LineCogs(0, Scaled(5m)).Should().Be(Money.Zero, "an open item has no cost");
    }

    [Fact]
    public void FR_9_4_GrossProfitIsNetMinusCogsAndMayBeNegative()
    {
        CanonicalFigures.GrossProfit(M(944m), M(546m)).Should().Be(M(398m));
        CanonicalFigures.GrossProfit(M(214.50m), M(260m)).Should().Be(M(-45.50m));
    }

    [Fact]
    public void FR_9_4_MarginIsGrossProfitOverNetAndIsZeroNotADivisionByZeroWhenNetIsZero()
    {
        CanonicalFigures.MarginRate(M(944m), M(398m)).Should().Be(398m / 944m);
        CanonicalFigures.MarginRate(M(214.50m), M(-45.50m)).Should().Be(-45.5m / 214.5m);
        CanonicalFigures.MarginRate(Money.Zero, Money.Zero).Should().Be(0m);
        CanonicalFigures.MarginRate(Money.Zero, M(10m)).Should().Be(0m, "a zero net never divides");
    }

    [Fact]
    public void RPT_01_AverageBillValueIsDiscountedGrossOverBillsAndZeroWithNoBills()
    {
        CanonicalFigures.AverageBillValue(M(1650m), M(56m), 5).Should().Be(M(318.80m));
        CanonicalFigures.AverageBillValue(M(650m), Money.Zero, 2).Should().Be(M(325m));
        CanonicalFigures.AverageBillValue(Money.Zero, Money.Zero, 0).Should().Be(Money.Zero);
        CanonicalFigures.AverageBillValue(M(100m), Money.Zero, 0).Should().Be(Money.Zero, "no bills, so no division");
    }

    [Fact]
    public void RPT_02_ShareIsPartOverTotalAndZeroWhenTheTotalIsZero()
    {
        CanonicalFigures.Share(M(487.50m), M(1158.50m)).Should().Be(487.5m / 1158.5m);
        CanonicalFigures.Share(M(10m), Money.Zero).Should().Be(0m);
        CanonicalFigures.Share(Money.Zero, Money.Zero).Should().Be(0m);
    }

    [Fact]
    public void FR_9_6_SumOfNothingIsZeroAndSumOfMoneyIsExact()
    {
        CanonicalFigures.Sum([]).Should().Be(Money.Zero);
        CanonicalFigures.Sum([M(0.1m), M(0.2m), M(0.3m)]).Should().Be(M(0.6m), "decimal money has no binary drift");
    }
}
