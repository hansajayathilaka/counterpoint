using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-09, stock valuation (task P3-T06 "Do this" #1) - the P3-T06 extension of P2-T11's report: the value at the
/// selling price, the category filter, and a cost-times-quantity that is multiplied in C# and never rounded -
/// against the balances at the end of <see cref="StockCashDataset"/>:
/// </summary>
/// <remarks>
/// <code>
/// variant   qty          cost avg  value at cost     price   value at price
/// Drill     1000         149.50    149500.00         250.00  250000.00     (category Tools)
/// Bolt      1136          59.75     67876.00         100.00  113600.00     (Fasteners)
/// Nail       971           4.00      3884.00          10.00    9710.00
/// SKEL       100           9.00       900.00          12.50    1250.00
/// Washer     200           2.65       530.00           5.00    1000.00     (Machine screws, a child of Fasteners)
/// Gasket      20           9.25       185.00          20.00     400.00
/// Cord        12.3456      7.7777      96.02037312     12.49    154.196544  (12.3456 x 7.7777 is exact at eight places)
/// Hinge       10           8.00        80.00          15.00     150.00     (Tools)
/// total at cost 223051.02037312     total at price 376264.196544
/// </code>
/// </remarks>
[Collection(StockCashReportFixture.Name)]
public sealed class StockValuationReportTests(StockCashReportFixture fixture)
{
    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private IStockValuationQuery Query => fixture.Host.Resolve<IStockValuationQuery>();

    [Fact]
    public async Task RPT_09_ValueAtCostAndAtSellingPriceMatchTheHandWorkedLinesAndTotals()
    {
        var report = await Query.GetValuationAsync();

        report.Lines.Select(line => (line.Sku, line.QtyOnHandBase.Value, line.CostAvg, line.Value, line.SellingPrice, line.ValueAtSellingPrice)).Should().Equal(
        [
            ("RPT-DRILL-A", 1000m, M(149.50m), M(149500.00m), M(250.00m), M(250000.00m)),
            ("RPT-BOLT-A", 1136m, M(59.75m), M(67876.00m), M(100.00m), M(113600.00m)),
            ("RPT-NAIL-A", 971m, M(4.00m), M(3884.00m), M(10.00m), M(9710.00m)),
            ("SKEL-001-A", 100m, M(9.00m), M(900.00m), M(12.50m), M(1250.00m)),
            ("EXT-WASHER-A", 200m, M(2.65m), M(530.00m), M(5.00m), M(1000.00m)),
            ("EXT-GASKET-A", 20m, M(9.25m), M(185.00m), M(20.00m), M(400.00m)),
            ("EXT-CORD-A", 12.3456m, M(7.7777m), M(96.02037312m), M(12.49m), M(154.196544m)),
            ("EXT-HINGE-A", 10m, M(8.00m), M(80.00m), M(15.00m), M(150.00m)),
        ], "highest value first");

        report.TotalValue.Should().Be(M(223051.02037312m));
        report.TotalValueAtSellingPrice.Should().Be(M(376264.196544m));
        report.Lines.Select(line => line.CategoryName).Should().Equal(
            "Tools", "Fasteners", string.Empty, string.Empty, "Machine screws", string.Empty, string.Empty, "Tools");
        report.Lines.Select(line => line.BaseUomSymbol).Should().OnlyContain(symbol => symbol == "pc");
        report.Lines.Aggregate(Money.Zero, (sum, line) => sum + line.Value).Should().Be(report.TotalValue, "the lines add up to the total exactly");
    }

    [Fact]
    public async Task RPT_09_TheValueIsMultipliedInCSharpSoItEqualsTheScaledSqlProductOverOneHundredMillionExactlyOnFractionalQuantitiesAtNonRoundCosts()
    {
        var report = await Query.GetValuationAsync();

        // The whole stock_balance table, as the database's own scaled integers.
        var scaledCost = await fixture.Host.CountAsync("SELECT SUM(qty_base * cost_avg) FROM stock_balance;");
        var scaledPrice = await fixture.Host.CountAsync(
            "SELECT SUM(sb.qty_base * pv.price) FROM stock_balance sb JOIN product_variant pv ON pv.id = sb.product_variant_id;");

        report.TotalValue.Amount.Should().Be(scaledCost / 100_000_000m, "qty_base x cost_avg is 1e8-scaled, exactly");
        report.TotalValueAtSellingPrice.Amount.Should().Be(scaledPrice / 100_000_000m);

        // Eight decimal places survive: 12.3456 x 7.7777 = 96.02037312, not 96.0204 and not 96.02.
        var cord = report.Lines.Single(line => line.Sku == "EXT-CORD-A");
        (await fixture.Host.CountAsync(
            "SELECT qty_base * cost_avg FROM stock_balance WHERE product_variant_id = " + fixture.Data.CordVariantId + ";"))
            .Should().Be(9_602_037_312);
        cord.Value.Amount.Should().Be(96.02037312m);
        cord.ValueAtSellingPrice.Amount.Should().Be(154.196544m);
    }

    [Fact]
    public async Task RPT_09_ACategoryFilterMatchesTheCategoryAndItsChildren()
    {
        var data = fixture.Data;

        var fasteners = await Query.GetValuationAsync(new StockValuationFilter(data.Sales.FastenersCategoryId));
        fasteners.Lines.Select(line => line.Sku).Should().Equal(["RPT-BOLT-A", "EXT-WASHER-A"], "Bolt, and Washer in the child category Machine screws");
        fasteners.TotalValue.Should().Be(M(68406.00m), "67876.00 + 530.00");
        fasteners.TotalValueAtSellingPrice.Should().Be(M(114600.00m), "113600.00 + 1000.00");

        var screws = await Query.GetValuationAsync(new StockValuationFilter(data.MachineScrewsCategoryId));
        screws.Lines.Select(line => line.Sku).Should().Equal("EXT-WASHER-A");
        screws.TotalValue.Should().Be(M(530.00m));
        screws.TotalValueAtSellingPrice.Should().Be(M(1000.00m));

        var tools = await Query.GetValuationAsync(new StockValuationFilter(data.Sales.ToolsCategoryId));
        tools.Lines.Select(line => line.Sku).Should().Equal("RPT-DRILL-A", "EXT-HINGE-A");
        tools.TotalValue.Should().Be(M(149580.00m));
        tools.TotalValueAtSellingPrice.Should().Be(M(250150.00m));

        var none = await Query.GetValuationAsync(new StockValuationFilter(987654321));
        none.Lines.Should().BeEmpty();
        none.TotalValue.Should().Be(Money.Zero);
        none.TotalValueAtSellingPrice.Should().Be(Money.Zero);
    }

    [Fact]
    public async Task RPT_09_TheParameterlessOverloadIsTheUnfilteredReport()
    {
        var plain = await Query.GetValuationAsync();
        var filtered = await Query.GetValuationAsync(new StockValuationFilter());

        filtered.TotalValue.Should().Be(plain.TotalValue);
        filtered.TotalValueAtSellingPrice.Should().Be(plain.TotalValueAtSellingPrice);
        filtered.Lines.Select(line => line.ProductVariantId).Should().Equal(plain.Lines.Select(line => line.ProductVariantId));
    }

    [Fact]
    public async Task RPT_09_AVariantNeverStockedIsNotOnTheValuationAndRunningItChangesNothing()
    {
        var report = await Query.GetValuationAsync();

        report.Lines.Should().NotContain(line => line.ProductVariantId == fixture.Data.RivetVariantId, "no balance row, nothing on the shelf");
        report.Lines.Should().NotContain(line => line.ProductVariantId == fixture.Data.LabourVariantId);

        var before = await fixture.Host.CountAsync("SELECT COUNT(*) FROM stock_movement;");
        await Query.GetValuationAsync();
        (await fixture.Host.CountAsync("SELECT COUNT(*) FROM stock_movement;")).Should().Be(before, "a report never posts to the ledger");
    }
}
