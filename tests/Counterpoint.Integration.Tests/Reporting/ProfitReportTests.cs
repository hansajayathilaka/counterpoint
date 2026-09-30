using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-03, the profit report (task P3-T05 "Do this" #3, SRS FR-9.4), checked against the hand-worked
/// history in <see cref="SalesReportDataset"/>.
/// </summary>
/// <remarks>
/// COGS by product, at the snapshot cost: Bolt 6 x 60.00 sold - 2 x 60.00 returned SELLABLE = 240.00;
/// Drill 3 x 150.00 - 0 (the return was DAMAGED, so its cost is never recovered) = 450.00;
/// Nail 29 x 4.00 = 116.00; the open item has no cost, 0. Together 806.00.
/// </remarks>
[Collection(SalesReportFixture.Name)]
public sealed class ProfitReportTests(SalesReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private const decimal Tolerance = 0.0000001m;

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private Task<ProfitReport> RunAsync(ProfitGrouping grouping, ReportDateRange? range = null) =>
        fixture.Host.Resolve<IProfitReportQuery>().GetProfitReportAsync(range ?? BothDays, grouping);

    [Fact]
    public async Task RPT_03_TheHeadlineMatchesTheHandWorkedFigures()
    {
        var report = await RunAsync(ProfitGrouping.Month);

        report.Totals.NetSales.Should().Be(M(1158.50m));
        report.Totals.Cogs.Should().Be(M(806.00m), "546.00 on day one + 380.00 sold and 120.00 recovered on day two");
        report.Totals.GrossProfit.Should().Be(M(352.50m));
        report.Totals.MarginRate.Should().BeApproximately(352.50m / 1158.50m, Tolerance);
    }

    [Fact]
    public async Task RPT_03_ByDayRowsMatchTheHandWorkedFigures()
    {
        var report = await RunAsync(ProfitGrouping.Day);

        report.Rows.Should().HaveCount(2);

        var one = report.Rows[0];
        one.Name.Should().Be("2026-09-06");
        one.Rank.Should().BeNull("a period row is chronological, not ranked");
        one.Net.Should().Be(M(944.00m));
        one.Cogs.Should().Be(M(546.00m), "330.00 + 96.00 + 120.00");
        one.GrossProfit.Should().Be(M(398.00m));
        one.MarginRate.Should().BeApproximately(398m / 944m, Tolerance);
        one.Period.Should().Be(ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));

        var two = report.Rows[1];
        two.Name.Should().Be("2026-09-07");
        two.Net.Should().Be(M(214.50m));
        two.Cogs.Should().Be(M(260.00m), "300.00 + 80.00 sold - 60.00 (R1) - 0.00 (R2 damaged) - 60.00 (R3)");
        two.GrossProfit.Should().Be(M(-45.50m), "a day whose returns outweigh its sales loses money");
        two.MarginRate.Should().BeApproximately(-45.5m / 214.5m, Tolerance);

        one.ShareOfNet.Should().BeApproximately(944m / 1158.5m, Tolerance);
    }

    [Fact]
    public async Task RPT_03_ByMonthRollsTheDaysIntoOneRowClippedToTheRange()
    {
        var month = await RunAsync(ProfitGrouping.Month);

        var row = month.Rows.Should().ContainSingle().Subject;
        row.Name.Should().Be("2026-09");
        row.Net.Should().Be(M(1158.50m));
        row.Cogs.Should().Be(M(806.00m));
        row.GrossProfit.Should().Be(M(352.50m));
        row.Period.Should().Be(BothDays, "the month's drill-down range is clipped to the report range, never the whole month");
        row.ShareOfNet.Should().Be(1m);

        var clipped = await RunAsync(
            ProfitGrouping.Month, ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo));
        clipped.Rows.Single().Period.Should().Be(
            ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo),
            "drilling in must never list a bill the report excluded");
        clipped.Rows.Single().Net.Should().Be(M(214.50m));
    }

    [Fact]
    public async Task RPT_03_ByItemRowsAreRankedByGrossProfit()
    {
        var data = fixture.Data;

        var report = await RunAsync(ProfitGrouping.Item);

        report.Rows.Select(row => row.Name).Should().Equal(["Nail", "Bolt", "(Open items)", "Drill"], "ranked by gross profit: 134.00, 131.00, 50.00, 37.50");
        report.Rows.Select(row => row.Rank).Should().Equal([1, 2, 3, 4]);

        var nail = report.Rows[0];
        nail.Key.Should().Be(data.NailVariantId);
        nail.QtyBase.Value.Should().Be(29m);
        nail.Net.Should().Be(M(250.00m));
        nail.Cogs.Should().Be(M(116.00m), "29 pieces at the 4.00 cost snapshot - the box is costed per base piece");
        nail.GrossProfit.Should().Be(M(134.00m));
        nail.MarginRate.Should().BeApproximately(134m / 250m, Tolerance);

        var bolt = report.Rows[1];
        bolt.Net.Should().Be(M(371.00m));
        bolt.Cogs.Should().Be(M(240.00m), "both Bolt returns were SELLABLE, so both recover their cost");
        bolt.GrossProfit.Should().Be(M(131.00m));
        bolt.MarginRate.Should().BeApproximately(131m / 371m, Tolerance);

        var open = report.Rows[2];
        open.Key.Should().BeNull();
        open.Net.Should().Be(M(50.00m));
        open.Cogs.Should().Be(Money.Zero, "an open item carries no cost");
        open.GrossProfit.Should().Be(M(50.00m));
        open.MarginRate.Should().Be(1m);

        var drill = report.Rows[3];
        drill.Net.Should().Be(M(487.50m));
        drill.Cogs.Should().Be(M(450.00m), "the returned Drill was DAMAGED, so its 150.00 is never recovered");
        drill.GrossProfit.Should().Be(M(37.50m));
        drill.MarginRate.Should().BeApproximately(37.5m / 487.5m, Tolerance);
    }

    [Fact]
    public async Task RPT_03_ByCategoryFilesANoCategoryProductAndAnOpenItemTogether()
    {
        var report = await RunAsync(ProfitGrouping.Category);

        report.Rows.Select(row => row.Name).Should().Equal(["(No category)", "Fasteners", "Tools"], "profit 184.00, 131.00, 37.50");

        report.Rows[0].Key.Should().BeNull();
        report.Rows[0].Net.Should().Be(M(300.00m));
        report.Rows[0].Cogs.Should().Be(M(116.00m));
        report.Rows[0].GrossProfit.Should().Be(M(184.00m));
        report.Rows[0].MarginRate.Should().BeApproximately(184m / 300m, Tolerance);

        report.Rows[1].Key.Should().Be(fixture.Data.FastenersCategoryId);
        report.Rows[1].Net.Should().Be(M(371.00m));
        report.Rows[1].Cogs.Should().Be(M(240.00m));

        report.Rows[2].Key.Should().Be(fixture.Data.ToolsCategoryId);
        report.Rows[2].Net.Should().Be(M(487.50m));
        report.Rows[2].Cogs.Should().Be(M(450.00m));
    }

    [Fact]
    public async Task RPT_03_ByBrandFilesANoBrandProductAndAnOpenItemTogether()
    {
        var report = await RunAsync(ProfitGrouping.Brand);

        report.Rows.Select(row => row.Name).Should().Equal(["(No brand)", "Bosch", "Makita"]);
        report.Rows.Select(row => row.GrossProfit).Should().Equal([M(184.00m), M(131.00m), M(37.50m)]);
        report.Rows.Select(row => row.Net).Should().Equal([M(300.00m), M(371.00m), M(487.50m)]);
        report.Rows.Select(row => row.Cogs).Should().Equal([M(116.00m), M(240.00m), M(450.00m)]);
    }

    [Fact]
    public async Task RPT_03_EveryGroupingAddsUpToTheHeadline()
    {
        foreach (var grouping in Enum.GetValues<ProfitGrouping>())
        {
            var report = await RunAsync(grouping);

            report.Rows.Aggregate(Money.Zero, (sum, row) => sum + row.Net).Should().Be(
                report.Totals.NetSales, "{0} rows must add up to net sales", grouping);
            report.Rows.Aggregate(Money.Zero, (sum, row) => sum + row.Cogs).Should().Be(
                report.Totals.Cogs, "{0} rows must add up to COGS", grouping);
            report.Rows.Aggregate(Money.Zero, (sum, row) => sum + row.GrossProfit).Should().Be(
                report.Totals.GrossProfit, "{0} rows must add up to gross profit", grouping);
        }
    }

    [Fact]
    public async Task RPT_03_AnEmptyRangeIsAllZerosWithNoDivisionByZero()
    {
        var empty = ReportDateRange.Custom(new(2026, 1, 1), new(2026, 1, 31));

        foreach (var grouping in Enum.GetValues<ProfitGrouping>())
        {
            var report = await RunAsync(grouping, empty);

            report.Rows.Should().BeEmpty();
            report.Totals.NetSales.Should().Be(Money.Zero);
            report.Totals.Cogs.Should().Be(Money.Zero);
            report.Totals.GrossProfit.Should().Be(Money.Zero);
            report.Totals.MarginRate.Should().Be(0m, "a zero net sales figure gives a zero margin, not a division by zero");
        }
    }

    [Fact]
    public async Task RPT_01_AnEmptyRangeIsAllZerosWithNoDivisionByZeroInAverageBill()
    {
        var report = await fixture.Host.Resolve<ISalesSummaryReportQuery>()
            .GetSummaryAsync(ReportDateRange.Custom(new(2026, 1, 1), new(2026, 1, 31)));

        report.Totals.BillCount.Should().Be(0);
        report.Totals.ReturnCount.Should().Be(0);
        report.Totals.GrossSales.Should().Be(Money.Zero);
        report.Totals.NetSales.Should().Be(Money.Zero);
        report.Totals.TenderTotal.Should().Be(Money.Zero);
        report.AverageBillValue.Should().Be(Money.Zero);
        report.ByDay.Should().BeEmpty();
        report.ByHour.Should().BeEmpty();
        report.ByTender.Should().BeEmpty();
    }
}
