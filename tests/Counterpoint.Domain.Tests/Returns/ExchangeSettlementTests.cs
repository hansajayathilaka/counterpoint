using System;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Returns;

/// <summary>
/// Netting a linked return's value against a replacement sale's value into one settlement (SRS
/// FR-5 exchange, AC-04, task P2-T04).
/// </summary>
public sealed class ExchangeSettlementTests
{
    [Fact]
    public void AC_04_AHigherPricedReplacementOwesExactlyTheDifference()
    {
        // Returned: 25.00. Replacement: 40.00. The customer owes exactly 15.00, and the whole
        // 25.00 is consumed as credit - nothing left to refund.
        var settlement = ExchangeSettlement.Calculate(
            Money.FromDecimal(25.00m), Money.FromDecimal(40.00m));

        settlement.CreditApplied.Should().Be(Money.FromDecimal(25.00m));
        settlement.AmountOwed.Should().Be(Money.FromDecimal(15.00m));
        settlement.LeftoverRefund.Should().Be(Money.Zero);
    }

    [Fact]
    public void ALowerPricedReplacementRefundsExactlyTheSurplus()
    {
        // Returned: 40.00. Replacement: 25.00. The replacement is fully covered by credit, and
        // the shop owes the customer the 15.00 the return was worth beyond that.
        var settlement = ExchangeSettlement.Calculate(
            Money.FromDecimal(40.00m), Money.FromDecimal(25.00m));

        settlement.CreditApplied.Should().Be(Money.FromDecimal(25.00m));
        settlement.AmountOwed.Should().Be(Money.Zero);
        settlement.LeftoverRefund.Should().Be(Money.FromDecimal(15.00m));
    }

    [Fact]
    public void AnEvenExchangeOwesNothingAndRefundsNothing()
    {
        var settlement = ExchangeSettlement.Calculate(
            Money.FromDecimal(25.00m), Money.FromDecimal(25.00m));

        settlement.CreditApplied.Should().Be(Money.FromDecimal(25.00m));
        settlement.AmountOwed.Should().Be(Money.Zero);
        settlement.LeftoverRefund.Should().Be(Money.Zero);
    }

    [Fact]
    public void AmountOwedAndLeftoverRefundAreNeverBothPositive()
    {
        var higher = ExchangeSettlement.Calculate(Money.FromDecimal(10.00m), Money.FromDecimal(30.00m));
        var lower = ExchangeSettlement.Calculate(Money.FromDecimal(30.00m), Money.FromDecimal(10.00m));
        var even = ExchangeSettlement.Calculate(Money.FromDecimal(10.00m), Money.FromDecimal(10.00m));

        (higher.AmountOwed.IsPositive && higher.LeftoverRefund.IsPositive).Should().BeFalse();
        (lower.AmountOwed.IsPositive && lower.LeftoverRefund.IsPositive).Should().BeFalse();
        (even.AmountOwed.IsPositive && even.LeftoverRefund.IsPositive).Should().BeFalse();
    }

    [Fact]
    public void ANegativeReturnValueIsRefused()
    {
        var act = () => ExchangeSettlement.Calculate(Money.FromDecimal(-1.00m), Money.FromDecimal(10.00m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ANegativeReplacementValueIsRefused()
    {
        var act = () => ExchangeSettlement.Calculate(Money.FromDecimal(10.00m), Money.FromDecimal(-1.00m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
