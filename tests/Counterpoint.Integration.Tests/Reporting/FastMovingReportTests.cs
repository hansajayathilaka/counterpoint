using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-13, fast-moving items (task P3-T06 "Do this" #1): the top sellers by units and by value, a projection of the
/// sales-by-item report, against its hand figures (see <c>SalesBreakdownReportTests</c>): over Sep 6-7 Drill 487.50
/// (2 net units), Bolt 371.00 (4), Nail 250.00 (29 pieces), the open-item Delivery 50.00 (not an item); on Sep 7 alone
/// Drill 250.00 (1), Nail 50.00 (5) and Bolt -85.50 (-1: one sold, two returned).
/// </summary>
[Collection(StockCashReportFixture.Name)]
public sealed class FastMovingReportTests(StockCashReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private IFastMovingReportQuery Query => fixture.Host.Resolve<IFastMovingReportQuery>();

    [Fact]
    public async Task RPT_13_TheTopSellersAreRankedByValueAndByUnitsWithTheOpenItemLeftOut()
    {
        var data = fixture.Data.Sales;

        var report = await Query.GetReportAsync(BothDays);

        report.TopN.Should().Be(20);
        report.ByValue.Select(row => (row.Rank, row.Sku, row.Name, row.Net, row.QtyBase.Value)).Should().Equal(
        [
            (1, "RPT-DRILL-A", "Drill", M(487.50m), 2m),
            (2, "RPT-BOLT-A", "Bolt", M(371.00m), 4m),
            (3, "RPT-NAIL-A", "Nail", M(250.00m), 29m),
        ]);
        report.ByUnits.Select(row => (row.Rank, row.Sku, row.QtyBase.Value, row.Net)).Should().Equal(
        [
            (1, "RPT-NAIL-A", 29m, M(250.00m)),
            (2, "RPT-BOLT-A", 4m, M(371.00m)),
            (3, "RPT-DRILL-A", 2m, M(487.50m)),
        ]);

        report.ByValue.Select(row => row.ProductVariantId).Should().Equal(data.DrillVariantId, data.BoltVariantId, data.NailVariantId);
        report.ByUnits[0].UomSymbol.Should().Be("pc");
        report.ByValue.Should().NotContain(row => row.ProductVariantId == null, "the open-item Delivery is not an item");
        report.ByUnits.Should().NotContain(row => row.ProductVariantId == null);

        // It reconciles to the sales-by-item report: the three items plus the open item are the canonical net sales.
        var breakdown = await fixture.Host.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(BothDays, SalesBreakdownDimension.Item);
        (report.ByValue.Aggregate(Money.Zero, (sum, row) => sum + row.Net) + M(50.00m)).Should().Be(breakdown.TotalNet);
    }

    [Fact]
    public async Task RPT_13_TopNTruncatesEachRankingAndRanksStayOneBased()
    {
        var report = await Query.GetReportAsync(BothDays, topN: 2);

        report.TopN.Should().Be(2);
        report.ByValue.Select(row => (row.Rank, row.Sku)).Should().Equal([(1, "RPT-DRILL-A"), (2, "RPT-BOLT-A")]);
        report.ByUnits.Select(row => (row.Rank, row.Sku)).Should().Equal([(1, "RPT-NAIL-A"), (2, "RPT-BOLT-A")]);

        var one = await Query.GetReportAsync(BothDays, topN: 1);
        one.ByValue.Select(row => row.Sku).Should().Equal("RPT-DRILL-A");
        one.ByUnits.Select(row => row.Sku).Should().Equal("RPT-NAIL-A");
    }

    [Fact]
    public async Task RPT_13_AnItemWithNoNetUnitsOrNoNetValueIsLeftOutOfThatRanking()
    {
        // Sep 7: Bolt sold 1 and had 2 back - net -1 unit and -85.50.
        var report = await Query.GetReportAsync(ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo));

        report.ByValue.Select(row => (row.Sku, row.Net)).Should().Equal([("RPT-DRILL-A", M(250.00m)), ("RPT-NAIL-A", M(50.00m))]);
        report.ByUnits.Select(row => (row.Sku, row.QtyBase.Value)).Should().Equal([("RPT-NAIL-A", 5m), ("RPT-DRILL-A", 1m)]);
        report.ByValue.Should().NotContain(row => row.Sku == "RPT-BOLT-A");
        report.ByUnits.Should().NotContain(row => row.Sku == "RPT-BOLT-A");
    }

    [Fact]
    public async Task RPT_13_ARangeWithNoSalesHasEmptyRankings()
    {
        var report = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 1, 1), new(2026, 1, 31)));

        report.ByValue.Should().BeEmpty();
        report.ByUnits.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task RPT_13_ANonPositiveTopNIsRefused(int topN)
    {
        Func<Task> act = () => Query.GetReportAsync(BothDays, topN);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
}
