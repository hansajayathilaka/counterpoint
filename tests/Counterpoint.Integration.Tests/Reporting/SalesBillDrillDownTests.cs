using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The drill-down half of RPT-01/RPT-02 (task P3-T05 "Do this" #5): a summary row leads to a bill
/// list, a bill-list row leads to one bill. "Drill-down from a summary row reaches the correct bill."
/// </summary>
[Collection(SalesReportFixture.Name)]
public sealed class SalesBillDrillDownTests(SalesReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    [Fact]
    public async Task RPT_01_DrillingADayRowListsExactlyThatDaysCompletedBillsOldestFirst()
    {
        var data = fixture.Data;
        var summary = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);
        var dayOne = summary.ByDay.Single(row => row.BusinessDate == SalesReportDataset.DayOne);

        var list = await fixture.Host.Resolve<ISalesBillQuery>().GetBillsAsync(
            new BillListFilter(ReportDateRange.Custom(dayOne.BusinessDate, dayOne.BusinessDate)));

        list.IsTruncated.Should().BeFalse();
        list.Rows.Select(row => row.SaleId).Should().Equal(
            [data.B1.SaleId, data.B2.SaleId, data.B3.SaleId], "the cancelled B4 is not listed, and the order is oldest first");
        list.Rows.Select(row => row.BillNo).Should().Equal(["INV-2026-000001", "INV-2026-000002", "INV-2026-000003"]);
        list.Rows.Should().HaveCount(dayOne.BillCount, "the list and the summary row count the same bills");

        // Before returns, the bills' own net adds up to the day row's gross less discounts.
        CanonicalNet(list).Should().Be(dayOne.Gross - dayOne.Discounts);
    }

    [Fact]
    public async Task RPT_01_ABillListRowCarriesTheHandWorkedBillFigures()
    {
        var list = await fixture.Host.Resolve<ISalesBillQuery>().GetBillsAsync(
            new BillListFilter(ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne)));

        var b1 = list.Rows[0];
        b1.SaleId.Should().Be(fixture.Data.B1.SaleId);
        b1.BusinessDate.Should().Be(SalesReportDataset.DayOne);
        b1.SoldAt.Should().Be(SalesReportDataset.At(6, 10, 5));
        b1.CustomerName.Should().Be("Walk-in");
        b1.CashierName.Should().Be("Shop Owner");
        b1.Gross.Should().Be(M(550.00m), "520.00 subtotal + 30.00 line discount");
        b1.Discounts.Should().Be(M(56.00m));
        b1.Tax.Should().Be(M(49.40m));
        b1.Net.Should().Be(M(494.00m));
        b1.Total.Should().Be(M(543.40m));
        b1.ReturnedSubtotal.Should().Be(M(85.50m), "R1, taken a day later, is against this bill");

        list.Rows[1].ReturnedSubtotal.Should().Be(Money.Zero);
        list.Rows[2].Net.Should().Be(M(200.00m));
    }

    [Fact]
    public async Task RPT_01_DrillingAnHourRowListsOnlyBillsSoldInThatHourOfDay()
    {
        var data = fixture.Data;
        var bills = fixture.Host.Resolve<ISalesBillQuery>();
        var summary = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);

        var ten = await bills.GetBillsAsync(new BillListFilter(BothDays, Hour: 10));
        ten.Rows.Select(row => row.SaleId).Should().Equal([data.B1.SaleId, data.B2.SaleId]);
        ten.Rows.Count.Should().Be(summary.ByHour.Single(row => row.Hour == 10).BillCount);

        var fourteen = await bills.GetBillsAsync(new BillListFilter(BothDays, Hour: 14));
        fourteen.Rows.Select(row => row.SaleId).Should().Equal(
            [data.B3.SaleId, data.B6.SaleId], "B3 on day one then B6 on day two - same hour of day, oldest first");

        (await bills.GetBillsAsync(new BillListFilter(BothDays, Hour: 9))).Rows
            .Select(row => row.SaleId).Should().Equal([data.B5.SaleId]);
    }

    [Fact]
    public async Task RPT_01_AnHourWithOnlyReturnsOrOnlyACancelledBillListsNoBills()
    {
        var bills = fixture.Host.Resolve<ISalesBillQuery>();

        (await bills.GetBillsAsync(new BillListFilter(BothDays, Hour: 11))).Rows
            .Should().BeEmpty("hour 11 holds two returns and no bill");
        (await bills.GetBillsAsync(new BillListFilter(BothDays, Hour: 15))).Rows
            .Should().BeEmpty("hour 15 holds only the cancelled B4");
    }

    [Fact]
    public async Task RPT_02_DrillingAnItemRowListsExactlyTheBillsWithALineForThatVariant()
    {
        var data = fixture.Data;
        var bills = fixture.Host.Resolve<ISalesBillQuery>();

        (await bills.GetBillsAsync(new BillListFilter(BothDays, ProductVariantId: data.BoltVariantId))).Rows
            .Select(row => row.SaleId).Should().Equal([data.B1.SaleId, data.B3.SaleId, data.B6.SaleId]);

        (await bills.GetBillsAsync(new BillListFilter(BothDays, ProductVariantId: data.NailVariantId))).Rows
            .Select(row => row.SaleId).Should().Equal([data.B2.SaleId, data.B6.SaleId], "the box sale and the piece sale");

        (await bills.GetBillsAsync(new BillListFilter(BothDays, ProductVariantId: data.DrillVariantId))).Rows
            .Select(row => row.SaleId).Should().Equal(
                [data.B1.SaleId, data.B5.SaleId], "the cancelled B4 also held a drill but is not a bill");
    }

    [Fact]
    public async Task RPT_02_TheItemFilterAndTheHourFilterCombine()
    {
        var data = fixture.Data;

        var list = await fixture.Host.Resolve<ISalesBillQuery>().GetBillsAsync(
            new BillListFilter(BothDays, Hour: 14, ProductVariantId: data.NailVariantId));

        list.Rows.Select(row => row.SaleId).Should().Equal([data.B6.SaleId], "B3 is at 14:20 but has no nails");
    }

    [Fact]
    public async Task RPT_01_TheBillListStopsAtTheCapAndSaysSo()
    {
        var bills = fixture.Host.Resolve<ISalesBillQuery>();

        var cut = await bills.GetBillsAsync(new BillListFilter(BothDays, MaxRows: 2));
        cut.Rows.Should().HaveCount(2);
        cut.IsTruncated.Should().BeTrue("five bills matched and only two were asked for");
        cut.Rows.Select(row => row.SaleId).Should().Equal([fixture.Data.B1.SaleId, fixture.Data.B2.SaleId], "the OLDEST two are kept");

        var exact = await bills.GetBillsAsync(new BillListFilter(BothDays, MaxRows: 5));
        exact.Rows.Should().HaveCount(5);
        exact.IsTruncated.Should().BeFalse("exactly five bills matched, so nothing was cut");

        var roomy = await bills.GetBillsAsync(new BillListFilter(BothDays, MaxRows: 6));
        roomy.IsTruncated.Should().BeFalse();
    }

    [Fact]
    public async Task RPT_01_AnInvalidBillListFilterIsRefused()
    {
        var bills = fixture.Host.Resolve<ISalesBillQuery>();

        Func<Task> zero = () => bills.GetBillsAsync(new BillListFilter(BothDays, MaxRows: 0));
        await zero.Should().ThrowAsync<ArgumentOutOfRangeException>();

        Func<Task> badHour = () => bills.GetBillsAsync(new BillListFilter(BothDays, Hour: 24));
        await badHour.Should().ThrowAsync<ArgumentOutOfRangeException>();

        Func<Task> negativeHour = () => bills.GetBillsAsync(new BillListFilter(BothDays, Hour: -1));
        await negativeHour.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task RPT_01_ABillDetailReturnsTheRightLinesPaymentsAndReturns()
    {
        var data = fixture.Data;

        var detail = await fixture.Host.Resolve<ISalesBillQuery>().GetBillAsync(data.B1.SaleId);

        detail.Should().NotBeNull();
        detail!.BillNo.Should().Be(data.B1.BillNo);
        detail.Status.Should().Be("COMPLETED");
        detail.SoldAt.Should().Be(SalesReportDataset.At(6, 10, 5));
        detail.BusinessDate.Should().Be(SalesReportDataset.DayOne);
        detail.CashierName.Should().Be("Shop Owner");
        detail.CustomerName.Should().Be("Walk-in");
        detail.Subtotal.Should().Be(M(520.00m));
        detail.LineDiscount.Should().Be(M(30.00m));
        detail.BillDiscount.Should().Be(M(26.00m));
        detail.Tax.Should().Be(M(49.40m));
        detail.Rounding.Should().Be(Money.Zero);
        detail.Total.Should().Be(M(543.40m));

        detail.Lines.Should().HaveCount(2);
        var bolt = detail.Lines[0];
        bolt.LineNo.Should().Be(1);
        bolt.Description.Should().Contain("Bolt");
        bolt.Quantity.Value.Should().Be(3m);
        bolt.UomSymbol.Should().Be("pc");
        bolt.UnitPrice.Should().Be(M(100.00m));
        bolt.Discount.Should().Be(M(30.00m));
        bolt.Tax.Should().Be(M(25.65m), "10% of 270.00 less its 13.50 share of the bill discount");
        bolt.LineTotal.Should().Be(M(270.00m));
        bolt.QuantityReturnedBase.Value.Should().Be(1m, "R1 returned one of the three");

        var drill = detail.Lines[1];
        drill.Description.Should().Contain("Drill");
        drill.Quantity.Value.Should().Be(1m);
        drill.UnitPrice.Should().Be(M(250.00m));
        drill.Discount.Should().Be(Money.Zero);
        drill.Tax.Should().Be(M(23.75m), "10% of 250.00 less its 12.50 share");
        drill.LineTotal.Should().Be(M(250.00m));
        drill.QuantityReturnedBase.Value.Should().Be(0m);

        detail.Payments.Select(payment => (payment.TenderType, payment.Amount)).Should().Equal(
            [("CASH", M(400.00m)), ("CARD", M(143.40m))]);

        var returned = detail.Returns.Should().ContainSingle().Subject;
        returned.ReturnNo.Should().Be(data.R1.ReturnNo);
        returned.BusinessDate.Should().Be(SalesReportDataset.DayTwo);
        returned.Subtotal.Should().Be(M(85.50m));
        returned.TotalRefund.Should().Be(M(94.05m));
        returned.RefundMethod.Should().Be("CASH");
    }

    [Fact]
    public async Task RPT_01_ABillDetailShowsAMultiUnitLineAndAnOpenItemAsSold()
    {
        var detail = await fixture.Host.Resolve<ISalesBillQuery>().GetBillAsync(fixture.Data.B2.SaleId);

        detail!.Lines.Should().HaveCount(2);

        var nails = detail.Lines[0];
        nails.Description.Should().Contain("Nail");
        nails.Quantity.Value.Should().Be(2m, "two boxes - the selling unit, not 24 base pieces");
        nails.UomSymbol.Should().Be("box");
        nails.UnitPrice.Should().Be(M(100.00m), "the box's own selling price, not 12 x 10.00");
        nails.LineTotal.Should().Be(M(200.00m));

        var delivery = detail.Lines[1];
        delivery.Description.Should().Be("Delivery");
        delivery.Quantity.Value.Should().Be(1m);
        delivery.UnitPrice.Should().Be(M(50.00m));
        delivery.Tax.Should().Be(Money.Zero, "an open item is taxed at the shop default, which is 0%");
        delivery.LineTotal.Should().Be(M(50.00m));
    }

    [Fact]
    public async Task RPT_01_ADetailIsReachableForACancelledBillAndMarkedCancelled()
    {
        var detail = await fixture.Host.Resolve<ISalesBillQuery>().GetBillAsync(fixture.Data.B4Cancelled.SaleId);

        detail.Should().NotBeNull("a cancelled bill keeps its number and its record (CLAUDE.md invariant 4)");
        detail!.Status.Should().Be("CANCELLED");
        detail.Total.Should().Be(M(275.00m));
    }

    [Fact]
    public async Task RPT_01_AnUnknownBillIsNullNotAnError()
    {
        (await fixture.Host.Resolve<ISalesBillQuery>().GetBillAsync(long.MaxValue)).Should().BeNull();
    }

    [Fact]
    public async Task RPT_01_EveryBillInTheListOpensToItsOwnDetail()
    {
        var bills = fixture.Host.Resolve<ISalesBillQuery>();
        var list = await bills.GetBillsAsync(new BillListFilter(BothDays));

        foreach (var row in list.Rows)
        {
            var detail = await bills.GetBillAsync(row.SaleId);

            detail!.SaleId.Should().Be(row.SaleId);
            detail.BillNo.Should().Be(row.BillNo);
            detail.Total.Should().Be(row.Total);
            detail.Lines.Aggregate(Money.Zero, (sum, line) => sum + line.LineTotal).Should().Be(
                detail.Subtotal, "the lines on a bill add up to its subtotal");
        }
    }

    private static Money CanonicalNet(SalesBillList list) =>
        list.Rows.Aggregate(Money.Zero, (sum, row) => sum + row.Net);
}
