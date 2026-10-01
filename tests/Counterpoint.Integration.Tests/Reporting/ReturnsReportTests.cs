using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The returns report (task P3-T05 "Do this" #4, SRS section 9 RPT-14): by reason, item,
/// disposition, refund method and linked versus unlinked, with the return rate against sales -
/// checked against the hand-worked history in <see cref="SalesReportDataset"/>.
/// </summary>
/// <remarks>
/// Returns, pre-tax: R1 85.50 (Bolt, SELLABLE, "Changed mind", linked, cash), R2 250.00 (Drill,
/// DAMAGED, "Cracked housing", linked, cash), R3 100.00 (Bolt, SELLABLE, "No receipt", unlinked,
/// card). Together 435.50; the cash refunded is 94.05 + 275.00 + 110.00 = 479.05.
/// </remarks>
[Collection(SalesReportFixture.Name)]
public sealed class ReturnsReportTests(SalesReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private const decimal Tolerance = 0.0000001m;

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private Task<ReturnsReport> RunAsync(ReportDateRange? range = null) =>
        fixture.Host.Resolve<IReturnsReportQuery>().GetReturnsReportAsync(range ?? BothDays);

    [Fact]
    public async Task RPT_14_TheHeadlineAndTheRateAgainstSalesMatchTheHandWorkedFigures()
    {
        var report = await RunAsync();

        report.ReturnCount.Should().Be(3);
        report.ReturnsSubtotal.Should().Be(M(435.50m), "85.50 + 250.00 + 100.00, pre-tax - what net sales subtracts");
        report.TotalRefunded.Should().Be(M(479.05m), "94.05 + 275.00 + 110.00 paid out, tax included");
        report.SalesBeforeReturns.Should().Be(M(1594.00m), "net sales 1158.50 + 435.50 returned");
        report.BillCount.Should().Be(5);
        report.ValueReturnRate.Should().BeApproximately(435.50m / 1594.00m, Tolerance);
        report.CountReturnRate.Should().Be(0.6m, "3 returns against 5 bills");
    }

    [Fact]
    public async Task RPT_14_ByReasonGroupsTheReturnLinesLargestValueFirst()
    {
        var report = await RunAsync();

        report.ByReason.Select(row => (row.Key, row.Count, row.QtyBase.Value, row.Value)).Should().Equal(
            [
                ("Cracked housing", 1, 1m, M(250.00m)),
                ("No receipt", 1, 1m, M(100.00m)),
                ("Changed mind", 1, 1m, M(85.50m)),
            ]);
        report.ByReason[0].ShareOfValue.Should().BeApproximately(250m / 435.5m, Tolerance);
        report.ByReason[2].ShareOfValue.Should().BeApproximately(85.5m / 435.5m, Tolerance);
    }

    [Fact]
    public async Task RPT_14_ByItemGroupsTheReturnLinesByProductVariant()
    {
        var report = await RunAsync();

        report.ByItem.Select(row => (row.Key, row.Count, row.QtyBase.Value, row.Value)).Should().Equal(
            [
                ("RPT-DRILL-A - Drill", 1, 1m, M(250.00m)),
                ("RPT-BOLT-A - Bolt", 2, 2m, M(185.50m)),
            ],
            "the two Bolt returns (one linked, one unlinked) are one item");
    }

    [Fact]
    public async Task RPT_14_ByDispositionSeparatesSellableFromDamaged()
    {
        var report = await RunAsync();

        report.ByDisposition.Select(row => (row.Key, row.Count, row.QtyBase.Value, row.Value)).Should().Equal(
            [
                ("DAMAGED", 1, 1m, M(250.00m)),
                ("SELLABLE", 2, 2m, M(185.50m)),
            ]);
        report.ByDisposition[0].ShareOfValue.Should().BeApproximately(250m / 435.5m, Tolerance);
    }

    [Fact]
    public async Task RPT_14_ByLinkageSeparatesLinkedFromUnlinkedReturns()
    {
        var report = await RunAsync();

        report.ByLinkage.Select(row => (row.Key, row.Count, row.Value)).Should().Equal(
            [
                ("Linked", 2, M(335.50m)),
                ("Unlinked", 1, M(100.00m)),
            ],
            "R1 and R2 name a bill, R3 does not");
        report.ByLinkage[1].ShareOfValue.Should().BeApproximately(100m / 435.5m, Tolerance);
    }

    [Fact]
    public async Task RPT_14_ByRefundMethodGroupsTheReturnsByHowTheyWerePaidOut()
    {
        var report = await RunAsync();

        report.ByRefundMethod.Select(row => (row.Key, row.Count, row.Value)).Should().Equal(
            [
                ("CASH", 2, M(335.50m)),
                ("CARD", 1, M(100.00m)),
            ]);
    }

    [Fact]
    public async Task RPT_14_AReturnIsCountedOnItsOwnDayNotTheDayOfTheBillItReturns()
    {
        var dayOne = await RunAsync(ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));

        dayOne.ReturnCount.Should().Be(0, "R1 returns a day-one bill but is dated day two");
        dayOne.ReturnsSubtotal.Should().Be(Money.Zero);
        dayOne.BillCount.Should().Be(3);
        dayOne.ValueReturnRate.Should().Be(0m);
        dayOne.CountReturnRate.Should().Be(0m);

        var dayTwo = await RunAsync(ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo));

        dayTwo.ReturnCount.Should().Be(3);
        dayTwo.SalesBeforeReturns.Should().Be(M(650.00m), "day two's net 214.50 + 435.50 returned");
        dayTwo.CountReturnRate.Should().Be(1.5m, "3 returns against 2 bills - more returns than bills is legitimate");
    }

    [Fact]
    public async Task RPT_14_AnEmptyRangeIsAllZerosWithNoDivisionByZero()
    {
        var report = await RunAsync(ReportDateRange.Custom(new(2026, 1, 1), new(2026, 1, 31)));

        report.ReturnCount.Should().Be(0);
        report.ReturnsSubtotal.Should().Be(Money.Zero);
        report.TotalRefunded.Should().Be(Money.Zero);
        report.SalesBeforeReturns.Should().Be(Money.Zero);
        report.BillCount.Should().Be(0);
        report.ValueReturnRate.Should().Be(0m);
        report.CountReturnRate.Should().Be(0m);
        report.ByReason.Should().BeEmpty();
        report.ByItem.Should().BeEmpty();
        report.ByDisposition.Should().BeEmpty();
        report.ByLinkage.Should().BeEmpty();
        report.ByRefundMethod.Should().BeEmpty();
    }
}
