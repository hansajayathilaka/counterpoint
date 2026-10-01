using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// Raw-versus-rollup routing for the P3-T05 headlines: a range that spans a closed, rolled-up day
/// and the open shift must equal the same range computed from raw tables only (P3-T04's "Done
/// when", carried into every P3-T05 report that quotes a headline).
/// </summary>
[Collection(SalesReportFixture.Name)]
public sealed class SalesReportRoutingTests(SalesReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    [Fact]
    public async Task P3_T05_TheDatasetReallyStraddlesARolledUpDayAndTheOpenShift()
    {
        // The premise of every routing assertion below, asserted rather than assumed.
        (await fixture.Host.CountAsync("SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(1, "closing shift 1 wrote day one's rollup");
        (await fixture.Host.CountAsync("SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-07';"))
            .Should().Be(0, "day two's shift is still open, so day two has no rollup");
        (await fixture.Host.CountAsync("SELECT COUNT(*) FROM shift WHERE status = 'OPEN';")).Should().Be(1);
    }

    [Fact]
    public async Task RPT_01_TheSalesSummaryHeadlineEqualsTheSameRangeReadFromRawTablesOnly()
    {
        var report = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);
        var raw = await fixture.Host.Resolve<ISalesPeriodSummaryQuery>()
            .GetSalesSummaryAsync(BothDays, ReportSourcePolicy.RawTablesRequired);

        report.Totals.Should().Be(raw);
    }

    [Fact]
    public async Task RPT_03_TheProfitHeadlineEqualsTheSameRangeReadFromRawTablesOnly()
    {
        var profit = fixture.Host.Resolve<IProfitPeriodSummaryQuery>();
        var raw = await profit.GetProfitSummaryAsync(BothDays, ReportSourcePolicy.RawTablesRequired);

        foreach (var grouping in System.Enum.GetValues<ProfitGrouping>())
        {
            var report = await fixture.Host.Resolve<IProfitReportQuery>().GetProfitReportAsync(BothDays, grouping);

            report.Totals.Should().Be(raw, "the {0} profit headline must not depend on the rollup/raw split", grouping);
        }
    }

    [Fact]
    public async Task RPT_14_TheReturnsReportRateDenominatorEqualsTheRawSalesFigure()
    {
        var report = await fixture.Host.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(BothDays);
        var raw = await fixture.Host.Resolve<ISalesPeriodSummaryQuery>()
            .GetSalesSummaryAsync(BothDays, ReportSourcePolicy.RawTablesRequired);

        report.SalesBeforeReturns.Should().Be(raw.NetSales + report.ReturnsSubtotal);
        report.BillCount.Should().Be(raw.BillCount);
        report.ReturnCount.Should().Be(raw.ReturnCount);
        report.TotalRefunded.Should().Be(raw.ReturnsValue);
    }

    [Fact]
    public async Task RPT_01_EachClosedDayAloneReadsTheSameFromItsRollupAndFromRaw()
    {
        var summary = fixture.Host.Resolve<ISalesSummaryReportQuery>();
        var day = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne);

        var routed = (await summary.GetSummaryAsync(day)).Totals;
        var raw = await fixture.Host.Resolve<ISalesPeriodSummaryQuery>()
            .GetSalesSummaryAsync(day, ReportSourcePolicy.RawTablesRequired);

        routed.Should().Be(raw);
        routed.NetSales.Should().Be(Money.FromDecimal(944.00m));
    }

    [Fact]
    public async Task RPT_01_AFieldByFieldSplitOfTheTotalsAgreesWithTheSliceSum()
    {
        var report = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);

        report.ByDay.Sum(row => row.BillCount).Should().Be(report.Totals.BillCount);
        report.ByDay.Sum(row => row.ReturnCount).Should().Be(report.Totals.ReturnCount);
        report.ByHour.Sum(row => row.BillCount).Should().Be(report.Totals.BillCount);
    }
}
