using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-01, the sales summary (task P3-T05, SRS section 9 RPT-01, FR-9.6): by day, by hour, by bill
/// and by tender, each figure checked against the hand-worked history in
/// <see cref="SalesReportDataset"/>.
/// </summary>
[Collection(SalesReportFixture.Name)]
public sealed class SalesSummaryReportTests(SalesReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    [Fact]
    public async Task RPT_01_TheHeadlineTotalsMatchTheHandWorkedFigures()
    {
        var report = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);

        // Day one 1000.00 + day two 650.00 gross; 56.00 of discounts (30.00 line + 26.00 bill) on B1.
        report.Totals.BillCount.Should().Be(5, "B1, B2, B3, B5, B6 - the cancelled B4 is not a bill");
        report.Totals.ReturnCount.Should().Be(3);
        report.Totals.GrossSales.Should().Be(M(1650.00m));
        report.Totals.Discounts.Should().Be(M(56.00m));
        report.Totals.Tax.Should().Be(M(129.40m), "49.40 + 20.00 on day one, 50.00 + 10.00 on day two");
        report.Totals.NetSales.Should().Be(M(1158.50m), "944.00 + 650.00 - 435.50 returned (pre-tax)");
        report.Totals.ReturnsValue.Should().Be(M(479.05m), "94.05 + 275.00 + 110.00 refunded");
        report.Totals.TenderTotal.Should().Be(M(1244.35m), "1013.40 + 710.00 tendered - 479.05 refunded");
        report.AverageBillValue.Should().Be(M(318.80m), "(1650.00 - 56.00) / 5 bills");
    }

    [Fact]
    public async Task RPT_01_ByDayRowsMatchTheHandWorkedFigures()
    {
        var report = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);

        report.ByDay.Should().HaveCount(2);

        var one = report.ByDay[0];
        one.BusinessDate.Should().Be(SalesReportDataset.DayOne);
        one.BillCount.Should().Be(3);
        one.ReturnCount.Should().Be(0, "every return is dated by its own day, which is day two");
        one.Gross.Should().Be(M(1000.00m));
        one.Discounts.Should().Be(M(56.00m));
        one.Tax.Should().Be(M(69.40m));
        one.Net.Should().Be(M(944.00m));
        one.ReturnsValue.Should().Be(Money.Zero);
        one.AverageBillValue.Amount.Should().BeApproximately(314.6667m, 0.0001m, "(1000.00 - 56.00) / 3 bills");

        var two = report.ByDay[1];
        two.BusinessDate.Should().Be(SalesReportDataset.DayTwo);
        two.BillCount.Should().Be(2);
        two.ReturnCount.Should().Be(3);
        two.Gross.Should().Be(M(650.00m));
        two.Discounts.Should().Be(Money.Zero);
        two.Tax.Should().Be(M(60.00m));
        two.Net.Should().Be(M(214.50m), "650.00 - 435.50 returned pre-tax");
        two.ReturnsValue.Should().Be(M(479.05m));
        two.AverageBillValue.Should().Be(M(325.00m), "650.00 / 2 bills");
    }

    [Fact]
    public async Task RPT_01_ByHourRowsMatchTheHandWorkedFigures()
    {
        var report = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);

        report.ByHour.Select(row => row.Hour).Should().Equal(
            [9, 10, 11, 14, 16],
            "earliest first; hour 15 holds only the cancelled B4 and so has no row, hours 11 and 16 hold only returns");

        var nine = report.ByHour[0];
        nine.BillCount.Should().Be(1);
        nine.Gross.Should().Be(M(500.00m));
        nine.Tax.Should().Be(M(50.00m));
        nine.Net.Should().Be(M(500.00m));
        nine.AverageBillValue.Should().Be(M(500.00m));

        var ten = report.ByHour[1];
        ten.BillCount.Should().Be(2, "B1 and B2");
        ten.Gross.Should().Be(M(800.00m), "550.00 + 250.00");
        ten.Discounts.Should().Be(M(56.00m));
        ten.Tax.Should().Be(M(49.40m));
        ten.Net.Should().Be(M(744.00m), "494.00 + 250.00");
        ten.AverageBillValue.Should().Be(M(372.00m), "744.00 / 2");

        var eleven = report.ByHour[2];
        eleven.BillCount.Should().Be(0);
        eleven.Gross.Should().Be(Money.Zero);
        eleven.Net.Should().Be(M(-335.50m), "R1 85.50 + R2 250.00 returned in this hour, no sales");
        eleven.AverageBillValue.Should().Be(Money.Zero, "no bills, so no division");

        var two = report.ByHour[3];
        two.BillCount.Should().Be(2, "B3 at 14:20 and B6 at 14:10 - on different days, same hour of day");
        two.Gross.Should().Be(M(350.00m));
        two.Tax.Should().Be(M(30.00m));
        two.Net.Should().Be(M(350.00m));
        two.AverageBillValue.Should().Be(M(175.00m));

        var four = report.ByHour[4];
        four.BillCount.Should().Be(0);
        four.Net.Should().Be(M(-100.00m), "the unlinked return R3");
    }

    [Fact]
    public async Task RPT_01_ByTenderRowsMatchTheHandWorkedFigures()
    {
        var report = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);

        report.ByTender.Select(row => row.TenderType).Should().Equal(["CARD", "CASH"], "by name");

        var card = report.ByTender[0];
        card.SalesAmount.Should().Be(M(363.40m), "143.40 on B1 + 220.00 on B3");
        card.RefundsAmount.Should().Be(M(110.00m), "R3 refunded to card");
        card.NetAmount.Should().Be(M(253.40m));

        var cash = report.ByTender[1];
        cash.SalesAmount.Should().Be(M(1360.00m), "400.00 + 250.00 + 550.00 + 160.00");
        cash.RefundsAmount.Should().Be(M(369.05m), "94.05 + 275.00");
        cash.NetAmount.Should().Be(M(990.95m));

        (card.NetAmount + cash.NetAmount).Should().Be(report.Totals.TenderTotal);
    }

    [Fact]
    public async Task RPT_01_ASingleDayRangeReadsOnlyThatDay()
    {
        var dayTwo = await fixture.Host.Resolve<ISalesSummaryReportQuery>()
            .GetSummaryAsync(ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo));

        dayTwo.ByDay.Should().ContainSingle();
        dayTwo.Totals.BillCount.Should().Be(2);
        dayTwo.Totals.NetSales.Should().Be(M(214.50m));
        dayTwo.ByHour.Select(row => row.Hour).Should().Equal([9, 11, 14, 16], "day one's 10:00 and 14:00 bills are outside the range");
        dayTwo.ByTender.Should().HaveCount(2);
    }
}
