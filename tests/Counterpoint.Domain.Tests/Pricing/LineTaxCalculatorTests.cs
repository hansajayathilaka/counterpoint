using System;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Pricing;

/// <summary>
/// One line's tax, for both tax-exclusive and tax-inclusive shops, rounded exactly once (SRS
/// FR-10.3, task P1-T08 step 4 and its documented risk: "compute line tax from the inclusive
/// price as price - price/(1+rate), round once at the line").
/// </summary>
public sealed class LineTaxCalculatorTests
{
    private static readonly IRoundingPolicy Rounding = new HalfAwayFromZeroRounding(2);

    [Fact]
    public void FR_10_3_AnExclusiveLineTotalsQuantityTimesPriceRoundedOnceAndTaxIsAddedOnTop()
    {
        // 3 units at 3.335, exclusive of a 15% tax: net = 10.005, rounded once to 10.01.
        var pricing = LineTaxCalculator.Calculate(
            Money.FromDecimal(3.335m), 3m, TaxRate.FromPercent(15m), pricesIncludeTax: false, Rounding);

        pricing.LineTotal.Should().Be(Money.FromDecimal(10.01m));
        pricing.Tax.Should().Be(Money.FromDecimal(1.5015m), "tax is computed off the rounded net, itself never rounded a second time");
    }

    [Fact]
    public void FR_10_3_AnInclusiveLineCarvesTaxOutOfTheRoundedGrossAndTheNetIsWhatIsLeft()
    {
        // 2 units at 5.75, tax-inclusive at 15%: gross = 11.50 (already a round number), tax
        // carved out is 11.50 - 11.50/1.15 = 1.5, net = 10.00.
        var pricing = LineTaxCalculator.Calculate(
            Money.FromDecimal(5.75m), 2m, TaxRate.FromPercent(15m), pricesIncludeTax: true, Rounding);

        pricing.LineTotal.Should().Be(Money.FromDecimal(10m));
        pricing.Tax.Should().Be(Money.FromDecimal(1.5m));
        (pricing.LineTotal + pricing.Tax).Should().Be(Money.FromDecimal(11.5m), "net plus tax reconstructs the rounded gross exactly");
    }

    [Fact]
    public void FR_10_3_AnInclusiveLineWhoseGrossDoesNotDivideExactlyStillReconstructsFromStoredValues()
    {
        // 1 unit at 9.99, inclusive of 15%: gross rounds to 9.99, tax = 9.99 - 9.99/1.15.
        var pricing = LineTaxCalculator.Calculate(
            Money.FromDecimal(9.99m), 1m, TaxRate.FromPercent(15m), pricesIncludeTax: true, Rounding);

        (pricing.LineTotal.ToScaled() + pricing.Tax.ToScaled()).Should().Be(
            pricing.ChargedTotal.ToScaled(),
            "line_total + tax must reconstruct the amount actually charged over the scaled, stored values - not merely as decimals in memory");
    }

    [Fact]
    public void FR_10_3_AZeroRatedLineHasNoTaxInEitherMode()
    {
        var exclusive = LineTaxCalculator.Calculate(Money.FromDecimal(10m), 3m, TaxRate.Zero, pricesIncludeTax: false, Rounding);
        var inclusive = LineTaxCalculator.Calculate(Money.FromDecimal(10m), 3m, TaxRate.Zero, pricesIncludeTax: true, Rounding);

        exclusive.Tax.Should().Be(Money.Zero);
        exclusive.LineTotal.Should().Be(Money.FromDecimal(30m));
        inclusive.Tax.Should().Be(Money.Zero);
        inclusive.LineTotal.Should().Be(Money.FromDecimal(30m));
    }

    [Fact]
    public void FR_10_3_RoundingHappensExactlyOnceAtTheLineNotOnTheUnrondedUnitPrice()
    {
        // An unrounded unit price (three fractional digits beyond the currency's two) times a
        // fractional quantity must be rounded only after multiplying - not per-unit first.
        var pricing = LineTaxCalculator.Calculate(
            Money.FromDecimal(0.3333m), 3m, TaxRate.Zero, pricesIncludeTax: false, Rounding);

        // 0.3333 * 3 = 0.9999, rounds once to 1.00 - not 0.33 rounded then multiplied (0.99).
        pricing.LineTotal.Should().Be(Money.FromDecimal(1.00m));
    }

    [Fact]
    public void ChargedTotalAlwaysReconstructsAsLineTotalPlusTaxInBothModes()
    {
        // The documented contract on LinePricing.ChargedTotal: "LineTotal plus Tax", regardless
        // of whether the shop prices inclusive or exclusive of tax.
        var exclusive = LineTaxCalculator.Calculate(
            Money.FromDecimal(7.77m), 4m, TaxRate.FromPercent(15m), pricesIncludeTax: false, Rounding);
        var inclusive = LineTaxCalculator.Calculate(
            Money.FromDecimal(7.77m), 4m, TaxRate.FromPercent(15m), pricesIncludeTax: true, Rounding);

        exclusive.ChargedTotal.Should().Be(exclusive.LineTotal + exclusive.Tax);
        inclusive.ChargedTotal.Should().Be(inclusive.LineTotal + inclusive.Tax);
    }

    [Fact]
    public void ANullRoundingPolicyIsRefused()
    {
        var act = () => LineTaxCalculator.Calculate(Money.FromDecimal(1m), 1m, TaxRate.Zero, true, null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
