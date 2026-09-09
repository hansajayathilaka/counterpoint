using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Pricing;

/// <summary>
/// The pure arithmetic <c>Counterpoint.Application.Pricing.DiscountAuthorisationService</c>
/// checks a discount against: the product's own cap wins over the shop-wide policy limit (SRS
/// FR-1.7, Q-12, task P1-T08 step 2).
/// </summary>
public sealed class DiscountCapPolicyTests
{
    [Fact]
    public void Q_12_TheProductsOwnCapWinsOverThePolicyLimitWhenBothAreSet()
    {
        var effective = DiscountCapPolicy.EffectiveCap(Percentage.FromPercent(5m), Percentage.FromPercent(20m));

        effective.Should().Be(Percentage.FromPercent(5m));
    }

    [Fact]
    public void Q_12_ThePolicyLimitAppliesWhenTheProductHasNoCapOfItsOwn()
    {
        var effective = DiscountCapPolicy.EffectiveCap(null, Percentage.FromPercent(20m));

        effective.Should().Be(Percentage.FromPercent(20m));
    }

    [Fact]
    public void Q_12_ADiscountAtExactlyTheCapDoesNotExceedIt()
    {
        var evaluation = DiscountCapPolicy.Evaluate(
            DiscountInput.OfRate(Percentage.FromPercent(5m)),
            Money.FromDecimal(100m),
            Percentage.FromPercent(5m),
            Percentage.OneHundredPercent);

        evaluation.ExceedsCap.Should().BeFalse("the boundary itself is still within the cap, not over it");
        evaluation.Rate.Should().Be(Percentage.FromPercent(5m));
        evaluation.Cap.Should().Be(Percentage.FromPercent(5m));
    }

    [Fact]
    public void Q_12_ATwelvePercentLineDiscountAgainstAFivePercentCapExceedsIt()
    {
        var evaluation = DiscountCapPolicy.Evaluate(
            DiscountInput.OfRate(Percentage.FromPercent(12m)),
            Money.FromDecimal(100m),
            Percentage.FromPercent(5m),
            Percentage.OneHundredPercent);

        evaluation.ExceedsCap.Should().BeTrue();
        evaluation.Amount.Should().Be(Money.FromDecimal(12m));
        evaluation.Cap.Should().Be(Percentage.FromPercent(5m));
    }

    [Fact]
    public void AFixedAmountDiscountIsCheckedAsAProportionOfTheBaseAmount()
    {
        // 15 off a 100 base is a 15% rate, checked the same way a keyed-in percentage would be.
        var evaluation = DiscountCapPolicy.Evaluate(
            DiscountInput.OfAmount(Money.FromDecimal(15m)),
            Money.FromDecimal(100m),
            productMaxDiscountRate: null,
            Percentage.FromPercent(10m));

        evaluation.Rate.Should().Be(Percentage.FromPercent(15m));
        evaluation.ExceedsCap.Should().BeTrue();
    }

    [Fact]
    public void AFixedAmountDiscountAgainstAZeroBaseResolvesToNoRateRatherThanAnInfiniteOne()
    {
        var evaluation = DiscountCapPolicy.Evaluate(
            DiscountInput.OfAmount(Money.FromDecimal(15m)),
            Money.Zero,
            productMaxDiscountRate: null,
            Percentage.FromPercent(10m));

        evaluation.Rate.Should().Be(Percentage.Zero);
        evaluation.ExceedsCap.Should().BeFalse();
    }

    [Fact]
    public void ANegativeDiscountAmountOrRateIsRefused()
    {
        var negativeAmount = () => DiscountInput.OfAmount(Money.FromDecimal(-1m));
        var negativeRate = () => DiscountInput.OfRate(Percentage.FromPercent(-1m));

        negativeAmount.Should().Throw<System.ArgumentOutOfRangeException>();
        negativeRate.Should().Throw<System.ArgumentOutOfRangeException>();
    }
}
