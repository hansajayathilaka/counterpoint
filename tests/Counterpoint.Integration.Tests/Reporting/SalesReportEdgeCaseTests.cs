using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The awkward ranges and states of the P3-T05 reports: no data at all, returns with no sales in
/// the range, and a cancelled bill the rollup still remembers. None may divide by zero, none may
/// count a cancelled bill, and every report must agree on it.
/// </summary>
public sealed class SalesReportEdgeCaseTests
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    [Fact]
    public async Task RPT_01_AFreshDatabaseWithNoTradingAtAllReportsZerosEverywhereAndNeverThrows()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var range = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

        var summary = await fixture.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(range);
        summary.Totals.BillCount.Should().Be(0);
        summary.Totals.NetSales.Should().Be(Money.Zero);
        summary.AverageBillValue.Should().Be(Money.Zero);
        summary.ByDay.Should().BeEmpty();
        summary.ByHour.Should().BeEmpty();
        summary.ByTender.Should().BeEmpty();

        foreach (var dimension in Enum.GetValues<SalesBreakdownDimension>())
        {
            var breakdown = await fixture.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(range, dimension);
            breakdown.Rows.Should().BeEmpty();
            breakdown.TotalNet.Should().Be(Money.Zero);
        }

        foreach (var grouping in Enum.GetValues<ProfitGrouping>())
        {
            var profit = await fixture.Resolve<IProfitReportQuery>().GetProfitReportAsync(range, grouping);
            profit.Rows.Should().BeEmpty();
            profit.Totals.MarginRate.Should().Be(0m);
        }

        var returns = await fixture.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(range);
        returns.ValueReturnRate.Should().Be(0m);
        returns.CountReturnRate.Should().Be(0m);

        (await fixture.Resolve<ISalesBillQuery>().GetBillsAsync(new BillListFilter(range))).Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task RPT_14_AReturnOnADayWithNoSalesGivesAZeroRateNotADivisionByZero()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var data = await SalesReportDataset.BuildAsync(fixture);

        // 2026-09-09: nothing sold, but one of B3's two bolts (100.00 net, 10.00 tax) comes back.
        var dayThree = new DateOnly(2026, 9, 9);
        var returned = await SalesReportDataset.ReturnAsync(
            fixture, data.B3, lineNo: 1, quantityBase: 1m, ReturnDisposition.Sellable, "Changed mind",
            SalesReportDataset.At(9, 10, 0), fixture.Resolve<Counterpoint.Application.Security.ISession>().ShiftId!.Value, data.OwnerId);
        returned.TotalRefund.Should().Be(M(110.00m));

        var range = ReportDateRange.Custom(dayThree, dayThree);

        var returns = await fixture.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(range);
        returns.ReturnCount.Should().Be(1);
        returns.ReturnsSubtotal.Should().Be(M(100.00m));
        returns.TotalRefunded.Should().Be(M(110.00m));
        returns.BillCount.Should().Be(0);
        returns.SalesBeforeReturns.Should().Be(Money.Zero, "net sales -100.00 plus 100.00 returned");
        returns.ValueReturnRate.Should().Be(0m, "no sales to be a rate of");
        returns.CountReturnRate.Should().Be(0m, "no bills to be a rate of");

        var summary = await fixture.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(range);
        summary.Totals.BillCount.Should().Be(0);
        summary.Totals.NetSales.Should().Be(M(-100.00m));
        summary.AverageBillValue.Should().Be(Money.Zero, "no bills, so no average");
        summary.ByDay.Should().ContainSingle().Which.ReturnCount.Should().Be(1);
        summary.ByHour.Should().ContainSingle().Which.Hour.Should().Be(10);

        var profit = await fixture.Resolve<IProfitReportQuery>().GetProfitReportAsync(range, ProfitGrouping.Day);
        profit.Totals.NetSales.Should().Be(M(-100.00m));
        profit.Totals.Cogs.Should().Be(M(-60.00m), "the sellable return recovers the 60.00 snapshot cost");
        profit.Totals.GrossProfit.Should().Be(M(-40.00m));
        profit.Totals.MarginRate.Should().Be(0.4m, "-40.00 / -100.00");

        (await fixture.Resolve<ISalesBillQuery>().GetBillsAsync(new BillListFilter(range))).Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task RPT_02_ACancelledBillOnARolledUpDateIsExcludedFromEveryReport()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var data = await SalesReportDataset.BuildAsync(fixture);

        // B2 (250.00, cash, 96.00 of cost) is cancelled after its shift closed and rolled up - the
        // stale-rollup state P3-T04 defends against and CancelSaleHandler no longer creates. Written
        // the only way it can still exist: the column-scoped UPDATE trg_sale_restricted_update permits.
        await fixture.ExecuteAsync(
            "UPDATE sale SET status = 'CANCELLED', cancelled_by = user_id, cancelled_at = '"
            + SalesReportDataset.At(6, 21, 0).ToString(Iso8601TimestampConverter.Format, CultureInfo.InvariantCulture)
            + "' WHERE id = " + data.B2.SaleId.ToString(CultureInfo.InvariantCulture) + ";");

        (await fixture.CountAsync("SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(1, "day one's rollup is still there, and is now stale");

        var summary = await fixture.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);

        // Hand figures less B2: 4 bills; gross 1650.00 - 250.00; net 1158.50 - 250.00; tender 1244.35 - 250.00.
        summary.Totals.BillCount.Should().Be(4);
        summary.Totals.GrossSales.Should().Be(M(1400.00m));
        summary.Totals.NetSales.Should().Be(M(908.50m));
        summary.Totals.TenderTotal.Should().Be(M(994.35m));
        summary.ByDay[0].BillCount.Should().Be(2);
        summary.ByDay[0].Net.Should().Be(M(694.00m), "494.00 + 200.00");
        summary.ByHour.Single(row => row.Hour == 10).BillCount.Should().Be(1, "only B1 remains at 10:xx");
        summary.ByTender.Single(row => row.TenderType == "CASH").SalesAmount.Should().Be(M(1110.00m), "1360.00 - 250.00");

        // The raw-only reading agrees with the routed one.
        (await fixture.Resolve<ISalesPeriodSummaryQuery>().GetSalesSummaryAsync(BothDays, ReportSourcePolicy.RawTablesRequired))
            .Should().Be(summary.Totals);

        var profit = await fixture.Resolve<IProfitReportQuery>().GetProfitReportAsync(BothDays, ProfitGrouping.Item);
        profit.Totals.Cogs.Should().Be(M(710.00m), "806.00 - 96.00");
        profit.Totals.GrossProfit.Should().Be(M(198.50m), "908.50 - 710.00");
        profit.Rows.Select(row => row.Name).Should().NotContain("(Open items)", "the only open-item line was on B2");
        profit.Rows.Single(row => row.Key == data.NailVariantId).Net.Should().Be(M(50.00m), "B6's five pieces only");

        var breakdown = await fixture.Resolve<ISalesBreakdownQuery>().GetBreakdownAsync(BothDays, SalesBreakdownDimension.Item);
        breakdown.TotalNet.Should().Be(M(908.50m));

        var bills = await fixture.Resolve<ISalesBillQuery>().GetBillsAsync(new BillListFilter(BothDays));
        bills.Rows.Select(row => row.SaleId).Should().Equal(
            [data.B1.SaleId, data.B3.SaleId, data.B5.SaleId, data.B6.SaleId]);

        (await fixture.Resolve<ISalesBillQuery>().GetBillAsync(data.B2.SaleId))!.Status.Should().Be("CANCELLED");

        var returns = await fixture.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(BothDays);
        returns.BillCount.Should().Be(4);
        returns.SalesBeforeReturns.Should().Be(M(1344.00m), "908.50 + 435.50");
        returns.CountReturnRate.Should().Be(0.75m, "3 returns against 4 bills");
    }
}
