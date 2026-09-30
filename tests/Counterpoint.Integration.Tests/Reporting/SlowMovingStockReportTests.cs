using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-12, slow-moving and dead stock (task P3-T06): idle time now runs from the last <c>SALE</c> movement - a bill
/// later cancelled does not count, and a receipt, stock take, adjustment or return never resets it; a variant that
/// never sold is measured from its first ledger movement; the value tied up is <c>qty x cost_avg</c>.
/// </summary>
/// <remarks>
/// <code>
/// variant  last counted sale        first movement   last movement (any type)   on hand x cost = value tied up
/// Gasket   never                    Sep 2 08:00      Sep 10 09:00 (G2)          20 x 9.25    =    185.00
/// Hinge    never (B7 cancelled)     Sep 2 08:00      Sep 11 10:10 (reversal)    10 x 8.00    =     80.00
/// Cord     never                    Sep 4 08:00      Sep 4 08:00                12.3456 x 7.7777 = 96.02037312
/// SKEL     never                    Sep 6 09:15      Sep 6 09:15                100 x 9.00   =    900.00
/// Drill    Sep 7 09:30 (B5)         Sep 1 08:00      Sep 11 10:00 (stock take)  1000 x 149.50 = 149500.00
/// Bolt     Sep 7 14:10 (B6)         Sep 1 08:00      Sep 13 00:00 (G4)          1136 x 59.75 =  67876.00
/// Nail     Sep 7 14:10 (B6)         Sep 1 08:00      Sep 7 14:10                971 x 4.00   =   3884.00
/// Washer   never                    Sep 10 09:00     Sep 12 23:59:59 (G3)       200 x 2.65   =    530.00
/// </code>
/// </remarks>
[Collection(StockCashReportFixture.Name)]
public sealed class SlowMovingStockReportTests(StockCashReportFixture fixture)
{
    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private static DateTimeOffset At(int day, int hour, int minute, int second = 0) => StockCashDataset.At(day, hour, minute, second);

    private ISlowMovingStockQuery Query => fixture.Host.Resolve<ISlowMovingStockQuery>();

    [Fact]
    public async Task RPT_12_IdleTimeRunsFromTheLastSaleSoAReceiptACountAndAnAdjustmentDoNotResetIt()
    {
        // Cutoff Sep 10 00:00. Bolt had a receipt on Sep 13, Drill a stock take on Sep 11 and adjustments on Sep 8-9:
        // by "any movement" neither would be idle; by last sale both are.
        var lines = await Query.FindAsync(new SlowMovingFilter(At(10, 0, 0)));

        lines.Select(line => line.Sku).Should().Equal(
            ["EXT-GASKET-A", "EXT-HINGE-A", "EXT-CORD-A", "SKEL-001-A", "RPT-DRILL-A", "RPT-BOLT-A", "RPT-NAIL-A"],
            "oldest idle first, ties by product code; Washer is not listed - its stock arrived on Sep 10");

        var drill = lines.Single(line => line.Sku == "RPT-DRILL-A");
        drill.LastSaleAt.Should().Be(At(7, 9, 30), "B5; the cancelled B4 and its reversal do not count");
        drill.IdleSince.Should().Be(At(7, 9, 30));
        drill.LastMovementAt.Should().Be(At(11, 10, 0), "the stock take is the last movement of any type, for information only");

        var bolt = lines.Single(line => line.Sku == "RPT-BOLT-A");
        bolt.LastSaleAt.Should().Be(At(7, 14, 10));
        bolt.IdleSince.Should().Be(At(7, 14, 10), "returns on Sep 7 16:00 and the Sep 13 receipt do not reset it");
        bolt.LastMovementAt.Should().Be(At(13, 0, 0));

        lines.Single(line => line.Sku == "RPT-NAIL-A").IdleSince.Should().Be(At(7, 14, 10));
    }

    [Fact]
    public async Task RPT_12_ABillLaterCancelledIsNotASaleAndTheVariantIsMeasuredFromItsFirstMovement()
    {
        var lines = await Query.FindAsync(new SlowMovingFilter(At(10, 0, 0)));

        // Hinge: rung up and cancelled on Sep 11 - it never sold.
        var hinge = lines.Single(line => line.Sku == "EXT-HINGE-A");
        hinge.LastSaleAt.Should().BeNull("a cancelled bill is not a sale");
        hinge.IdleSince.Should().Be(At(2, 8, 0), "never sold: measured from the first ledger movement");
        hinge.LastMovementAt.Should().Be(At(11, 10, 10), "the cancellation's reversal is the last movement of any type");

        // A variant that never sold at all is measured from the day its stock first arrived.
        var gasket = lines.Single(line => line.Sku == "EXT-GASKET-A");
        gasket.LastSaleAt.Should().BeNull();
        gasket.IdleSince.Should().Be(At(2, 8, 0), "its Sep 10 receipt does not restart the clock");
        gasket.LastMovementAt.Should().Be(At(10, 9, 0));

        lines.Single(line => line.Sku == "SKEL-001-A").IdleSince.Should().Be(new DateTimeOffset(2026, 9, 6, 9, 15, 0, SalesReportDataset.ShopOffset));
    }

    [Fact]
    public async Task RPT_12_AVariantWhoseStockArrivedAfterTheCutoffAndNeverSoldIsNotFlaggedDeadOnItsFirstDay()
    {
        var atCutoff = await Query.FindAsync(new SlowMovingFilter(At(10, 0, 0)));
        atCutoff.Should().NotContain(line => line.Sku == "EXT-WASHER-A", "its first movement is Sep 10 09:00, after the cutoff");

        var later = await Query.FindAsync(new SlowMovingFilter(At(10, 9, 0)));
        later.Should().Contain(line => line.Sku == "EXT-WASHER-A", "at exactly its first movement it is idle since then (the boundary is inclusive)");
        later.Single(line => line.Sku == "EXT-WASHER-A").IdleSince.Should().Be(At(10, 9, 0));
        later.Single(line => line.Sku == "EXT-WASHER-A").LastMovementAt.Should().Be(At(12, 23, 59, 59));
    }

    [Fact]
    public async Task RPT_12_AnEarlierCutoffListsOnlyWhatHasBeenIdleSinceThen()
    {
        // Sep 7 12:00: Drill (last sale 09:30) is idle; Bolt and Nail (last sale 14:10) are not.
        var lines = await Query.FindAsync(new SlowMovingFilter(At(7, 12, 0)));

        lines.Select(line => line.Sku).Should().Equal(
            ["EXT-GASKET-A", "EXT-HINGE-A", "EXT-CORD-A", "SKEL-001-A", "RPT-DRILL-A"]);

        (await Query.FindAsync(new SlowMovingFilter(At(1, 0, 0)))).Should().BeEmpty("nothing had been idle since before the first opening count");

        var everything = await Query.FindAsync(new SlowMovingFilter(new DateTimeOffset(2026, 12, 31, 0, 0, 0, SalesReportDataset.ShopOffset)));
        everything.Should().HaveCount(8, "every variant with stock on hand; Rivet and Labour have none");
        everything.Select(line => line.Sku).Last().Should().Be("EXT-WASHER-A", "idle since Sep 10 09:00, the most recent");
    }

    [Fact]
    public async Task RPT_12_TheValueTiedUpIsQuantityTimesCostAveragedMultipliedExactlyInCSharp()
    {
        var lines = await Query.FindAsync(new SlowMovingFilter(At(10, 0, 0)));

        lines.Select(line => (line.Sku, line.QtyOnHandBase.Value, line.CostAvg, line.ValueTiedUp)).Should().Equal(
        [
            ("EXT-GASKET-A", 20m, M(9.25m), M(185.00m)),
            ("EXT-HINGE-A", 10m, M(8.00m), M(80.00m)),
            ("EXT-CORD-A", 12.3456m, M(7.7777m), M(96.02037312m)),
            ("SKEL-001-A", 100m, M(9.00m), M(900.00m)),
            ("RPT-DRILL-A", 1000m, M(149.50m), M(149500.00m)),
            ("RPT-BOLT-A", 1136m, M(59.75m), M(67876.00m)),
            ("RPT-NAIL-A", 971m, M(4.00m), M(3884.00m)),
        ]);

        lines.Select(line => line.CategoryName).Should().Equal(string.Empty, "Tools", string.Empty, string.Empty, "Tools", "Fasteners", string.Empty);
        lines.Select(line => line.ProductDescription).Should().Equal("Gasket", "Hinge", "Cord", "Galvanised bolt M8", "Drill", "Bolt", "Nail");
        lines.Aggregate(Money.Zero, (sum, line) => sum + line.ValueTiedUp).Should().Be(M(222521.02037312m), "185 + 80 + 96.02037312 + 900 + 149500 + 67876 + 3884");
    }

    [Fact]
    public async Task RPT_12_ACategoryFilterMatchesTheCategoryAndItsChildren()
    {
        var farFuture = new DateTimeOffset(2026, 12, 31, 0, 0, 0, SalesReportDataset.ShopOffset);

        var fasteners = await Query.FindAsync(new SlowMovingFilter(farFuture, fixture.Data.Sales.FastenersCategoryId));
        fasteners.Select(line => line.Sku).Should().Equal("RPT-BOLT-A", "EXT-WASHER-A");

        var screws = await Query.FindAsync(new SlowMovingFilter(farFuture, fixture.Data.MachineScrewsCategoryId));
        screws.Select(line => line.Sku).Should().Equal("EXT-WASHER-A");

        var tools = await Query.FindAsync(new SlowMovingFilter(farFuture, fixture.Data.Sales.ToolsCategoryId));
        tools.Select(line => line.Sku).Should().Equal("EXT-HINGE-A", "RPT-DRILL-A");
    }

    [Fact]
    public async Task RPT_12_TheInstantOverloadIsTheUnfilteredReport()
    {
        var cutoff = At(10, 0, 0);

        var viaInstant = await Query.FindAsync(cutoff);
        var viaFilter = await Query.FindAsync(new SlowMovingFilter(cutoff));

        viaInstant.Select(line => line.ProductVariantId).Should().Equal(viaFilter.Select(line => line.ProductVariantId));
    }
}
