using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// Task P3-T05's named risk: "Margin computed from the current cost instead of the snapshot cost.
/// Use <c>sale_line.unit_cost</c> always." (CLAUDE.md invariant 10.) These tests move the catalogue
/// after the sales and prove no report moves with it.
/// </summary>
public sealed class SalesReportSnapshotTests
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    [Fact]
    public async Task RPT_03_ChangingAProductsCostAfterASaleDoesNotMoveProfitCogsOrMargin()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var data = await SalesReportDataset.BuildAsync(fixture);

        var profit = fixture.Resolve<IProfitReportQuery>();
        var before = new
        {
            Day = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Day),
            Month = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Month),
            Category = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Category),
            Brand = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Brand),
            Item = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Item),
        };

        // Sanity on the premise: these are the hand-worked figures before the catalogue moves.
        before.Item.Totals.Cogs.Should().Be(M(806.00m));
        before.Item.Totals.GrossProfit.Should().Be(M(352.50m));

        // Every product's cost goes up tenfold through the real inbound door (the same path a
        // goods receipt takes), which moves product.cost_avg and stock_balance.cost_avg.
        await ReceiveAtTenTimesTheCostAsync(fixture, data);

        (await fixture.ScalarAsync("SELECT cost_avg FROM product WHERE code = 'RPT-BOLT';"))
            .Should().NotBe(M(60.00m).ToScaled().ToString(CultureInfo.InvariantCulture), "the catalogue cost really did move");
        (await fixture.ScalarAsync(
            "SELECT DISTINCT unit_cost FROM sale_line WHERE product_variant_id = " + data.BoltVariantId + ";"))
            .Should().Be(M(60.00m).ToScaled().ToString(CultureInfo.InvariantCulture), "the snapshot on every Bolt line is untouched");

        var after = new
        {
            Day = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Day),
            Month = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Month),
            Category = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Category),
            Brand = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Brand),
            Item = await profit.GetProfitReportAsync(BothDays, ProfitGrouping.Item),
        };

        after.Should().BeEquivalentTo(before, "no profit figure may move when the catalogue cost does");
        after.Item.Totals.Cogs.Should().Be(M(806.00m));
        after.Item.Totals.GrossProfit.Should().Be(M(352.50m));
        after.Item.Totals.MarginRate.Should().BeApproximately(352.50m / 1158.50m, 0.0000001m);
        after.Day.Rows.Select(row => row.Cogs).Should().Equal([M(546.00m), M(260.00m)]);
        after.Item.Rows.Select(row => (row.Name, row.Cogs)).Should().BeEquivalentTo(
            [("Nail", M(116.00m)), ("Bolt", M(240.00m)), ("(Open items)", M(0m)), ("Drill", M(450.00m))]);

        // The P3-T04 period query is the same story, through the rollup (day one) and raw (day two).
        var period = await fixture.Resolve<IProfitPeriodSummaryQuery>().GetProfitSummaryAsync(BothDays);
        period.Cogs.Should().Be(M(806.00m));
        (await fixture.Resolve<IProfitPeriodSummaryQuery>().GetProfitSummaryAsync(BothDays, ReportSourcePolicy.RawTablesRequired))
            .Cogs.Should().Be(M(806.00m));
    }

    [Fact]
    public async Task RPT_03_ASaleMadeAfterTheCostChangeUsesTheNewCostAndOnlyThatSale()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var data = await SalesReportDataset.BuildAsync(fixture);
        await ReceiveAtTenTimesTheCostAsync(fixture, data);

        // The positive control: the cost change is live where sales read it, so a snapshot that
        // "did not move" above is meaningful and not just a cost change that never took effect.
        var late = await SalesReportDataset.SellAsync(
            fixture,
            SalesReportDataset.At(7, 17, 0),
            [new SaleLineRequest(data.BoltVariantId, 1m)],
            billDiscount: null,
            (TenderTypes.Cash, 110.00m));

        var lateCostScaled = await fixture.CountAsync(
            "SELECT unit_cost FROM sale_line WHERE sale_id = " + late.SaleId.ToString(CultureInfo.InvariantCulture) + ";");
        lateCostScaled.Should().NotBe(M(60.00m).ToScaled(), "the new sale snapshots today's cost, which is no longer 60.00");

        var report = await fixture.Resolve<IProfitReportQuery>().GetProfitReportAsync(BothDays, ProfitGrouping.Item);

        // Old sales stay at 60.00; the new sale contributes its own snapshot, nothing else moves.
        report.Totals.NetSales.Should().Be(M(1258.50m), "1158.50 + the new 100.00 sale");
        report.Totals.Cogs.Should().Be(M(806.00m) + Money.FromScaled(lateCostScaled));

        var bolt = report.Rows.Single(row => row.Key == data.BoltVariantId);
        bolt.Cogs.Should().Be(M(240.00m) + Money.FromScaled(lateCostScaled));
        report.Rows.Single(row => row.Key == data.DrillVariantId).Cogs.Should().Be(M(450.00m), "no new Drill sale, so no new Drill cost");
    }

    [Fact]
    public async Task RPT_03_ChangingAPriceOrANameAfterASaleMovesNeitherTheReportsNorTheBill()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var data = await SalesReportDataset.BuildAsync(fixture);

        var breakdown = fixture.Resolve<ISalesBreakdownQuery>();
        var bills = fixture.Resolve<ISalesBillQuery>();
        var netBefore = await breakdown.GetBreakdownAsync(BothDays, SalesBreakdownDimension.Item);
        var billBefore = await bills.GetBillAsync(data.B1.SaleId);

        await fixture.ExecuteAsync("UPDATE product_variant SET price = 999.0000 * 10000;");
        await fixture.ExecuteAsync("UPDATE product SET name = 'Renamed ' || name;");

        var netAfter = await breakdown.GetBreakdownAsync(BothDays, SalesBreakdownDimension.Item);
        var billAfter = await bills.GetBillAsync(data.B1.SaleId);

        // Net is priced from the sale lines, never from today's price list.
        netAfter.Rows.Select(row => (row.Key, row.Net)).Should().Equal(netBefore.Rows.Select(row => (row.Key, row.Net)));
        netAfter.TotalNet.Should().Be(M(1158.50m));

        // The bill shows the snapshot description and price it was sold with (invariant 10).
        billAfter.Should().BeEquivalentTo(billBefore);
        billAfter!.Lines[0].Description.Should().NotStartWith("Renamed");
        billAfter.Lines[0].UnitPrice.Should().Be(M(100.00m));
    }

    private static async Task ReceiveAtTenTimesTheCostAsync(SaleFixture fixture, SalesReportDataset data)
    {
        var ledger = fixture.Resolve<IStockLedger>();
        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;
        var at = SalesReportDataset.At(7, 16, 30);

        (long Variant, decimal Cost)[] receipts =
        [
            (data.BoltVariantId, 600.00m),
            (data.DrillVariantId, 1500.00m),
            (data.NailVariantId, 40.00m),
        ];

        foreach (var (variant, cost) in receipts)
        {
            await ledger.PostAsync(
                new StockPosting(
                    variant, "GRN", Quantity.FromDecimal(1000m, data.PieceUomId), Money.FromDecimal(cost),
                    "GRN", RefDocId: null, userId, at),
                CancellationToken.None);
        }
    }
}
