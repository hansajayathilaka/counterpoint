using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Inventory;

/// <summary>
/// The single step both <c>SqliteStockLedger.PostAsync</c> and <c>RebuildStockBalanceCommand</c>
/// run (P1-T07, SRS FR-4, DM-05).
/// </summary>
public sealed class StockLedgerMathTests
{
    private const long BaseUomId = 1;

    [Fact]
    public void FR_4_AReceiptSellReceiptSequenceMatchesTheHandWorkedExampleToFourDecimalPlaces()
    {
        var qty = Quantity.Zero(BaseUomId);
        var avg = Money.Zero;

        // Receipt 1: 100 arrive at 9.0000. Nothing on the shelf yet, so the average becomes 9.0000.
        var step1 = StockLedgerMath.Apply(qty, avg, Quantity.FromDecimal(100m, BaseUomId), Money.FromDecimal(9.0000m));
        step1.QtyAfter.Value.Should().Be(100m);
        step1.CostAvgAfter.Should().Be(Money.FromDecimal(9.0000m));
        step1.MovementUnitCost.Should().Be(Money.FromDecimal(9.0000m));
        (qty, avg) = (step1.QtyAfter, step1.CostAvgAfter);

        // Sell 30. The average is untouched, and the movement itself is recorded at 9.0000 -
        // the COGS snapshot - never at whatever the caller happened to pass in (999.0000 here,
        // deliberately implausible, to prove it is ignored).
        var step2 = StockLedgerMath.Apply(qty, avg, Quantity.FromDecimal(-30m, BaseUomId), Money.FromDecimal(999m));
        step2.QtyAfter.Value.Should().Be(70m);
        step2.CostAvgAfter.Should().Be(Money.FromDecimal(9.0000m));
        step2.MovementUnitCost.Should().Be(Money.FromDecimal(9.0000m));
        (qty, avg) = (step2.QtyAfter, step2.CostAvgAfter);

        // Receipt 2: 50 more arrive at 12.0000.
        // (70 * 9.0000 + 50 * 12.0000) / 120 = 1230 / 120 = 10.2500.
        var step3 = StockLedgerMath.Apply(qty, avg, Quantity.FromDecimal(50m, BaseUomId), Money.FromDecimal(12.0000m));
        step3.QtyAfter.Value.Should().Be(120m);
        step3.CostAvgAfter.Should().Be(Money.FromDecimal(10.2500m));
        step3.MovementUnitCost.Should().Be(Money.FromDecimal(12.0000m));
    }

    [Fact]
    public void FR_4_AnOutboundMovementNeverChangesTheAverage()
    {
        var step = StockLedgerMath.Apply(
            Quantity.FromDecimal(100m, BaseUomId),
            Money.FromDecimal(9m),
            Quantity.FromDecimal(-40m, BaseUomId),
            Money.FromDecimal(1234m));

        step.QtyAfter.Value.Should().Be(60m);
        step.CostAvgAfter.Should().Be(Money.FromDecimal(9m), "an outbound movement never recomputes the average");
        step.MovementUnitCost.Should().Be(
            Money.FromDecimal(9m),
            "the ledger records what was already on the shelf - the COGS snapshot - never the caller's value");
    }

    [Fact]
    public void FR_4_ANegativeResultingQuantityIsAllowedOnAnOutboundMovementQ11()
    {
        var step = StockLedgerMath.Apply(
            Quantity.FromDecimal(10m, BaseUomId),
            Money.FromDecimal(9m),
            Quantity.FromDecimal(-25m, BaseUomId),
            Money.Zero);

        step.QtyAfter.Value.Should().Be(-15m, "Q-11: negative stock is allowed, never blocked here");
        step.CostAvgAfter.Should().Be(Money.FromDecimal(9m));
    }

    [Fact]
    public void FR_4_AZeroQuantityMovementIsRejected()
    {
        var act = () => StockLedgerMath.Apply(
            Quantity.FromDecimal(10m, BaseUomId),
            Money.FromDecimal(9m),
            Quantity.Zero(BaseUomId),
            Money.FromDecimal(9m));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
