using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Inventory;

/// <summary>
/// The one formula behind the stock projection's moving-average cost column
/// (P1-T07, SRS FR-4, DM-05).
/// </summary>
public sealed class MovingAverageCostTests
{
    private const long BaseUomId = 1;

    [Fact]
    public void FR_4_MovingAverageCostMatchesTheTextbookFormula()
    {
        // 70 on the shelf at 9.0000, 50 more arrive at 12.0000:
        // (70 * 9.0000 + 50 * 12.0000) / 120 = (630 + 600) / 120 = 10.2500.
        var oldQty = Quantity.FromDecimal(70m, BaseUomId);
        var oldAvg = Money.FromDecimal(9.0000m);
        var inQty = Quantity.FromDecimal(50m, BaseUomId);
        var inCost = Money.FromDecimal(12.0000m);

        var newAvg = MovingAverageCost.Recompute(oldQty, oldAvg, inQty, inCost);

        newAvg.Should().Be(Money.FromDecimal(10.2500m));
    }

    [Fact]
    public void FR_4_TheFirstEverReceiptBecomesTheAverage()
    {
        // Nothing on the shelf yet: the average is simply the cost of what arrived.
        var newAvg = MovingAverageCost.Recompute(
            Quantity.Zero(BaseUomId),
            Money.Zero,
            Quantity.FromDecimal(100m, BaseUomId),
            Money.FromDecimal(9m));

        newAvg.Should().Be(Money.FromDecimal(9m));
    }

    [Fact]
    public void FR_4_ANonPositiveResultingQuantityFallsBackToTheArrivingCostRatherThanDividingByItsSign()
    {
        // Deep in negative stock (Q-11 allows it): -10 on the shelf, 5 arrive. The resulting
        // quantity is still non-positive, so there is no meaningful weighted average of it - the
        // average becomes the cost of what just arrived.
        var newAvg = MovingAverageCost.Recompute(
            Quantity.FromDecimal(-10m, BaseUomId),
            Money.FromDecimal(5m),
            Quantity.FromDecimal(5m, BaseUomId),
            Money.FromDecimal(8m));

        newAvg.Should().Be(Money.FromDecimal(8m));
    }

    [Fact]
    public void FR_4_AReceiptThatExactlyZeroesTheShelfFallsBackToTheArrivingCost()
    {
        // -5 on the shelf, exactly 5 arrive: oldQty + inQty is zero, the division this formula
        // would otherwise perform (CLAUDE.md invariant 1's arithmetic is exact, but zero is
        // still zero) is guarded against explicitly.
        var newAvg = MovingAverageCost.Recompute(
            Quantity.FromDecimal(-5m, BaseUomId),
            Money.FromDecimal(3m),
            Quantity.FromDecimal(5m, BaseUomId),
            Money.FromDecimal(11m));

        newAvg.Should().Be(Money.FromDecimal(11m));
    }

    [Fact]
    public void FR_4_OnlyAPositiveMovementRecomputesTheAverage()
    {
        var act = () => MovingAverageCost.Recompute(
            Quantity.FromDecimal(10m, BaseUomId),
            Money.FromDecimal(5m),
            Quantity.FromDecimal(-3m, BaseUomId),
            Money.FromDecimal(5m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
