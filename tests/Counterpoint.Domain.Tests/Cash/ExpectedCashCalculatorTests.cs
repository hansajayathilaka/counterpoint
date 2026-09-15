using Counterpoint.Domain.Cash;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Cash;

/// <summary>
/// The single expected-drawer formula (SRS FR-8.1, FR-8.3, FR-8.4, task P3-T01 "Do this" #2 and
/// its own "Risks": "two implementations of the expected-cash formula, one in X and one in Z" -
/// there must be exactly one, proven by exactly one test here).
/// </summary>
public sealed class ExpectedCashCalculatorTests
{
    /// <summary>
    /// A hand-worked example (task P3-T01 "Done when": "expected cash matches a hand-worked
    /// example including refunds, cash in and cash out"):
    ///
    /// opening float 5,000.00 + cash sales 12,340.50 - cash refunds 1,200.00 + cash in 2,000.00
    /// - cash out 3,500.00 = 14,640.50.
    /// </summary>
    [Fact]
    public void FR_8_1_ExpectedCashIsOpeningFloatPlusCashSalesLessRefundsPlusInLessOut()
    {
        var expected = ExpectedCashCalculator.Calculate(
            openingFloat: Money.FromDecimal(5000.00m),
            cashSales: Money.FromDecimal(12340.50m),
            cashRefunds: Money.FromDecimal(1200.00m),
            cashIn: Money.FromDecimal(2000.00m),
            cashOut: Money.FromDecimal(3500.00m));

        expected.Should().Be(Money.FromDecimal(14640.50m));
    }

    [Fact]
    public void ANewlyOpenedShiftWithNoActivityExpectsExactlyItsOwnFloat()
    {
        var expected = ExpectedCashCalculator.Calculate(
            openingFloat: Money.FromDecimal(5000.00m),
            cashSales: Money.Zero,
            cashRefunds: Money.Zero,
            cashIn: Money.Zero,
            cashOut: Money.Zero);

        expected.Should().Be(Money.FromDecimal(5000.00m));
    }

    [Fact]
    public void RefundsAndCashOutReduceTheExpectedTotalWhileSalesAndCashInIncreaseIt()
    {
        var baseline = ExpectedCashCalculator.Calculate(
            Money.FromDecimal(1000m), Money.Zero, Money.Zero, Money.Zero, Money.Zero);

        var withSales = ExpectedCashCalculator.Calculate(
            Money.FromDecimal(1000m), Money.FromDecimal(500m), Money.Zero, Money.Zero, Money.Zero);
        var withRefunds = ExpectedCashCalculator.Calculate(
            Money.FromDecimal(1000m), Money.Zero, Money.FromDecimal(500m), Money.Zero, Money.Zero);
        var withCashIn = ExpectedCashCalculator.Calculate(
            Money.FromDecimal(1000m), Money.Zero, Money.Zero, Money.FromDecimal(500m), Money.Zero);
        var withCashOut = ExpectedCashCalculator.Calculate(
            Money.FromDecimal(1000m), Money.Zero, Money.Zero, Money.Zero, Money.FromDecimal(500m));

        withSales.Should().Be(baseline + Money.FromDecimal(500m));
        withRefunds.Should().Be(baseline - Money.FromDecimal(500m));
        withCashIn.Should().Be(baseline + Money.FromDecimal(500m));
        withCashOut.Should().Be(baseline - Money.FromDecimal(500m));
    }
}
