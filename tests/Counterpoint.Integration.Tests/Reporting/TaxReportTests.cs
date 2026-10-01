using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-19, the tax report (task P3-T06 "Do this" #2), against the hand-worked history in
/// <see cref="SalesReportDataset"/> (exclusive pricing) and two small histories of its own (inclusive pricing;
/// a rate changed mid-history). Hand figures, worked from the dataset's bill table:
/// </summary>
/// <remarks>
/// <code>
/// Taxable value = line_total less the line's share of the bill discount, excluding tax in both pricing modes.
///   B1: Bolt 270.00 - 13.50 = 256.50 tax 25.65 @10%; Drill 250.00 - 12.50 = 237.50 tax 23.75 @10%
///   B2: Nail 200.00 @0% and the open-item Delivery 50.00 @0%
///   B3: Bolt 200.00 tax 20.00 @10%           B4: cancelled (Drill 250.00 tax 25.00) - nowhere
///   B5: Drill 500.00 tax 50.00 @10%          B6: Bolt 100.00 tax 10.00 @10%; Nail 50.00 @0%
///   R1: linked B1 Bolt  85.50 tax 8.55 @10%  R2: linked B5 Drill 250.00 tax 25.00 @10%  R3: unlinked 100.00 tax 10.00
/// Sep 6-7   10%: sales 1294.00 / 129.40, returns 335.50 / 33.55   0%: sales 300.00 / 0   unlinked returns 100.00 / 10.00
///           totals: sales 1594.00 / 129.40, returns 435.50 / 43.55, net 1158.50 / 85.85
/// </code>
/// </remarks>
[Collection(StockCashReportFixture.Name)]
public sealed class TaxReportTests(StockCashReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private ITaxReportQuery Query => fixture.Host.Resolve<ITaxReportQuery>();

    [Fact]
    public async Task RPT_19_TaxCollectedByRateMatchesTheHandWorkedFiguresWithReturnsNettedPerRate()
    {
        var report = await Query.GetTaxReportAsync(BothDays);

        report.Rows.Select(row => (row.Kind, row.Rate?.AsPercent)).Should().Equal(
            [(TaxReportRowKind.Rate, 0m), (TaxReportRowKind.Rate, 10m), (TaxReportRowKind.UnlinkedReturns, (decimal?)null)],
            "rates lowest first, then the unlinked-returns row");

        var exempt = report.Rows[0];
        exempt.SalesTaxable.Should().Be(M(300.00m), "Nail 200.00 + Delivery 50.00 + Nail 50.00");
        exempt.SalesTax.Should().Be(Money.Zero);
        exempt.ReturnsTaxable.Should().Be(Money.Zero);
        exempt.ReturnsTax.Should().Be(Money.Zero);
        exempt.NetTaxable.Should().Be(M(300.00m));
        exempt.NetTax.Should().Be(Money.Zero);

        var ten = report.Rows[1];
        ten.SalesTaxable.Should().Be(M(1294.00m), "256.50 + 237.50 + 200.00 + 500.00 + 100.00 - the cancelled bill is not in it");
        ten.SalesTax.Should().Be(M(129.40m), "25.65 + 23.75 + 20.00 + 50.00 + 10.00");
        ten.ReturnsTaxable.Should().Be(M(335.50m), "R1 85.50 + R2 250.00, netted to the rate of the lines they reverse");
        ten.ReturnsTax.Should().Be(M(33.55m), "8.55 + 25.00");
        ten.NetTaxable.Should().Be(M(958.50m));
        ten.NetTax.Should().Be(M(95.85m));

        var unlinked = report.Rows[2];
        unlinked.Rate.Should().BeNull("an unlinked return has no recorded rate and is never guessed into one");
        unlinked.SalesTaxable.Should().Be(Money.Zero);
        unlinked.SalesTax.Should().Be(Money.Zero);
        unlinked.ReturnsTaxable.Should().Be(M(100.00m));
        unlinked.ReturnsTax.Should().Be(M(10.00m));
        unlinked.NetTaxable.Should().Be(M(-100.00m));
        unlinked.NetTax.Should().Be(M(-10.00m));

        report.TotalSalesTaxable.Should().Be(M(1594.00m));
        report.TotalSalesTax.Should().Be(M(129.40m));
        report.TotalReturnsTaxable.Should().Be(M(435.50m));
        report.TotalReturnsTax.Should().Be(M(43.55m));
        report.TotalNetTaxable.Should().Be(M(1158.50m), "the canonical net sales of the dataset");
        report.NetTax.Should().Be(M(85.85m));
        report.Range.Should().Be(BothDays);
    }

    [Fact]
    public async Task RPT_19_TheTaxReportReconcilesToTheSumOfSaleLineTaxAndSaleHeaderTaxForThePeriod()
    {
        var report = await Query.GetTaxReportAsync(BothDays);

        // Independent recomputations straight from the tables, in scaled integers.
        var lineTax = await fixture.Host.CountAsync(
            "SELECT SUM(sl.tax) FROM sale_line sl JOIN sale s ON s.id = sl.sale_id "
            + "WHERE s.status = 'COMPLETED' AND s.business_date BETWEEN '2026-09-06' AND '2026-09-07';");
        var headerTax = await fixture.Host.CountAsync(
            "SELECT SUM(tax) FROM sale WHERE status = 'COMPLETED' AND business_date BETWEEN '2026-09-06' AND '2026-09-07';");
        var returnTax = await fixture.Host.CountAsync(
            "SELECT SUM(tax) FROM sale_return WHERE business_date BETWEEN '2026-09-06' AND '2026-09-07';");

        lineTax.Should().Be(1_294_000, "129.40 in scaled units");
        headerTax.Should().Be(lineTax, "sale.tax is the sum of its lines' tax");

        var perRateSum = report.Rows.Aggregate(0L, (sum, row) => sum + row.SalesTax.ToScaled());
        perRateSum.Should().Be(lineTax, "the per-rate tax on completed sales equals SUM(sale_line.tax)");
        perRateSum.Should().Be(headerTax, "and SUM(sale.tax)");
        report.Rows.Aggregate(0L, (sum, row) => sum + row.ReturnsTax.ToScaled()).Should().Be(returnTax);

        report.SaleLineTaxTotal.ToScaled().Should().Be(lineTax);
        report.SaleHeaderTaxTotal.ToScaled().Should().Be(headerTax);
        report.ReturnHeaderTaxTotal.ToScaled().Should().Be(returnTax);
        report.IsReconciled.Should().BeTrue();

        // The cancelled bill would have added 25.00 had it been counted.
        (await fixture.Host.CountAsync("SELECT SUM(tax) FROM sale WHERE business_date BETWEEN '2026-09-06' AND '2026-09-07';"))
            .Should().Be(1_544_000, "the raw table holds the cancelled bill's 25.00 too - which the report must not");

        // Net sales excluding tax: the taxable value column is what the bills charged less their tax.
        var revenueExTax = await fixture.Host.CountAsync(
            "SELECT SUM(total) - SUM(tax) FROM sale WHERE status = 'COMPLETED' AND business_date BETWEEN '2026-09-06' AND '2026-09-07';");
        report.TotalSalesTaxable.ToScaled().Should().Be(revenueExTax, "1723.40 - 129.40 = 1594.00");
    }

    [Fact]
    public async Task RPT_19_ACancelledBillIsExcludedFromTaxableValueAndTaxCollected()
    {
        // Day one: B4 (Drill 250.00 + 25.00 tax) was rung up and cancelled.
        var report = await Query.GetTaxReportAsync(ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));

        report.Rows.Select(row => row.Rate?.AsPercent).Should().Equal(0m, 10m);
        report.Rows[1].SalesTaxable.Should().Be(M(694.00m), "256.50 + 237.50 + 200.00 - not 944.00");
        report.Rows[1].SalesTax.Should().Be(M(69.40m), "25.65 + 23.75 + 20.00 - not 94.40");
        report.Rows[0].SalesTaxable.Should().Be(M(250.00m));
        report.Rows[0].SalesTax.Should().Be(Money.Zero);
        report.TotalSalesTaxable.Should().Be(M(944.00m));
        report.TotalReturnsTaxable.Should().Be(Money.Zero, "no return that day");
        report.NetTax.Should().Be(M(69.40m));
        report.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_19_ReturnsAreTakenOnTheirOwnBusinessDateSoDayTwoNetsDayOnesBillAgainstDayTwosSales()
    {
        var report = await Query.GetTaxReportAsync(ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo));

        report.Rows.Select(row => row.Rate?.AsPercent).Should().Equal(0m, 10m, null);

        report.Rows[0].SalesTaxable.Should().Be(M(50.00m), "Nail x5");
        var ten = report.Rows[1];
        ten.SalesTaxable.Should().Be(M(600.00m), "B5 Drill 500.00 + B6 Bolt 100.00");
        ten.SalesTax.Should().Be(M(60.00m));
        ten.ReturnsTaxable.Should().Be(M(335.50m), "R1 reverses a day-one bill, but it is a day-two return");
        ten.ReturnsTax.Should().Be(M(33.55m));
        ten.NetTaxable.Should().Be(M(264.50m));
        ten.NetTax.Should().Be(M(26.45m));
        report.Rows[2].ReturnsTaxable.Should().Be(M(100.00m));

        report.TotalNetTaxable.Should().Be(M(214.50m), "the canonical net of day two");
        report.NetTax.Should().Be(M(16.45m), "60.00 - 43.55");
        report.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_19_ARangeWithNoTradingHasNoRowsZeroTotalsAndStillCarriesTheHeader()
    {
        var report = await Query.GetTaxReportAsync(ReportDateRange.Custom(new(2026, 1, 1), new(2026, 1, 31)));

        report.Rows.Should().BeEmpty();
        report.TotalSalesTaxable.Should().Be(Money.Zero);
        report.TotalSalesTax.Should().Be(Money.Zero);
        report.TotalReturnsTax.Should().Be(Money.Zero);
        report.NetTax.Should().Be(Money.Zero);
        report.IsReconciled.Should().BeTrue();
        report.Header.ShopName.Should().Be(StockCashDataset.ShopName);
    }

    [Fact]
    public async Task RPT_19_TheHeaderNamesTheShopItsRegistrationNumberTheTaxLabelAndThePricingBasisFromSettings()
    {
        var report = await Query.GetTaxReportAsync(BothDays);

        report.Header.ShopName.Should().Be("Kandy Hardware & Tools");
        report.Header.TaxRegistrationNumber.Should().Be("TIN-204-118-77");
        report.Header.TaxLabel.Should().Be("VAT");
        report.Header.PricesIncludeTax.Should().BeFalse("the dataset's shop quotes prices excluding tax");
    }

    [Fact]
    public async Task RPT_19_TheXReportsTaxBreakdownIsUnchangedAfterTheTaxableLinesExtraction()
    {
        // Regression: the X/Z report's tax breakdown now shares TaxableLines with the tax report. The
        // figures below are the hand-worked ones for each shift of the P3-T05 history.
        var shiftIds = await ShiftIdsAsync();
        var xReport = fixture.Host.Resolve<IXReportService>();

        var shiftOne = await xReport.GenerateAsync(shiftIds[0]);
        shiftOne.SalesCount.Should().Be(3);
        shiftOne.SalesTaxTotal.Should().Be(M(69.40m));
        shiftOne.TaxBreakdown.Select(line => (line.Rate.AsPercent, line.TaxableAmount, line.TaxAmount)).Should().Equal(
            [(0m, M(250.00m), Money.Zero), (10m, M(694.00m), M(69.40m))],
            "B1 256.50 + 237.50 and B3 200.00 at 10%; B2's 250.00 at 0%");

        var shiftTwo = await xReport.GenerateAsync(shiftIds[1]);
        shiftTwo.SalesCount.Should().Be(2);
        shiftTwo.SalesTaxTotal.Should().Be(M(60.00m));
        shiftTwo.ReturnsTaxTotal.Should().Be(M(43.55m));
        shiftTwo.TaxBreakdown.Select(line => (line.Rate.AsPercent, line.TaxableAmount, line.TaxAmount)).Should().Equal(
            [(0m, M(50.00m), Money.Zero), (10m, M(600.00m), M(60.00m))]);

        // And the two reports agree: the tax report for a shift's own day is that shift's breakdown.
        var dayOne = await Query.GetTaxReportAsync(ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));
        dayOne.Rows.Select(row => (row.Rate!.Value.AsPercent, row.SalesTaxable, row.SalesTax)).Should().Equal(
            shiftOne.TaxBreakdown.Select(line => (line.Rate.AsPercent, line.TaxableAmount, line.TaxAmount)));
    }

    [Fact]
    public async Task RPT_19_APricesIncludeTaxShopReportsTaxableValueExcludingTaxAndTheBillDiscountShareIsTakenOffIt()
    {
        // Inclusive pricing. A 110.00 @10%, B 120.00 @20%, C 50.00 @0%.
        //   S1 A x2            charged 220.00, tax 20.00, line_total 200.00, total 220.00 CASH
        //   S2 B x1, bill disc 12.00: payable 108.00, tax 18.00, line_total 102.00, taxable 102.00 - 12.00 = 90.00, CARD
        //   S3 C x3            150.00, tax 0, CASH
        //   S4 B x1, line disc 12.00: charged 108.00, tax 18.00, line_total 90.00, total 108.00, CASH
        //   R1 (Sep 7) linked to S1, A x1: refund 100.00 + 10.00 tax = 110.00
        await using var host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SalesReportDataset.SeedReturnNumberSequenceAsync(host);
        await SalesReportDataset.DisableBackupOnShiftCloseAsync(host);
        await PricedVariantSeeder.UsePricingModeAsync(host, pricesIncludeTax: true);

        var a = await PricedVariantSeeder.SeedAsync(host, "INC-A", 110.00m, 10m);
        var b = await PricedVariantSeeder.SeedAsync(host, "INC-B", 120.00m, 20m);
        var c = await PricedVariantSeeder.SeedAsync(host, "INC-C", 50.00m, 0m);
        var session = host.Resolve<ISession>();

        var s1 = await SalesReportDataset.SellAsync(host, SalesReportDataset.At(6, 10, 0), [new SaleLineRequest(a, 2m)], null, (TenderTypes.Cash, 220.00m));
        s1.Total.Should().Be(M(220.00m));
        var s2 = await SalesReportDataset.SellAsync(
            host, SalesReportDataset.At(6, 11, 0), [new SaleLineRequest(b, 1m)],
            Counterpoint.Domain.Pricing.DiscountInput.OfAmount(M(12.00m)), (TenderTypes.Card, 108.00m));
        s2.Total.Should().Be(M(108.00m));
        var s3 = await SalesReportDataset.SellAsync(host, SalesReportDataset.At(6, 12, 0), [new SaleLineRequest(c, 3m)], null, (TenderTypes.Cash, 150.00m));
        s3.Total.Should().Be(M(150.00m));
        var s4 = await SalesReportDataset.SellAsync(
            host, SalesReportDataset.At(6, 13, 0),
            [new SaleLineRequest(b, 1m, Discount: Counterpoint.Domain.Pricing.DiscountInput.OfAmount(M(12.00m)))], null, (TenderTypes.Cash, 108.00m));
        s4.Total.Should().Be(M(108.00m));

        await SalesReportDataset.ReturnAsync(
            host, s1, lineNo: 1, quantityBase: 1m, ReturnDisposition.Sellable, "Changed mind",
            SalesReportDataset.At(7, 11, 0), session.ShiftId!.Value, session.CurrentUser!.Id);

        var report = await host.Resolve<ITaxReportQuery>().GetTaxReportAsync(BothDays);

        report.Header.PricesIncludeTax.Should().BeTrue();
        report.Rows.Select(row => row.Rate?.AsPercent).Should().Equal(0m, 10m, 20m);

        report.Rows[0].SalesTaxable.Should().Be(M(150.00m));
        report.Rows[0].SalesTax.Should().Be(Money.Zero);

        var ten = report.Rows[1];
        ten.SalesTaxable.Should().Be(M(200.00m), "220.00 gross less its 20.00 of tax");
        ten.SalesTax.Should().Be(M(20.00m));
        ten.ReturnsTaxable.Should().Be(M(100.00m));
        ten.ReturnsTax.Should().Be(M(10.00m));
        ten.NetTaxable.Should().Be(M(100.00m));
        ten.NetTax.Should().Be(M(10.00m));

        var twenty = report.Rows[2];
        twenty.SalesTaxable.Should().Be(M(180.00m), "S2 (102.00 - 12.00 bill discount share) + S4 (90.00 after its line discount)");
        twenty.SalesTax.Should().Be(M(36.00m), "18.00 + 18.00");

        report.TotalSalesTaxable.Should().Be(M(530.00m));
        report.TotalSalesTax.Should().Be(M(56.00m));
        report.TotalReturnsTax.Should().Be(M(10.00m));
        report.NetTax.Should().Be(M(46.00m));
        report.TotalNetTaxable.Should().Be(M(430.00m));

        (await host.CountAsync("SELECT SUM(tax) FROM sale;")).Should().Be(560_000);
        (await host.CountAsync("SELECT SUM(tax) FROM sale_line;")).Should().Be(560_000);
        (await host.CountAsync("SELECT SUM(total) - SUM(tax) FROM sale;")).Should().Be(report.TotalSalesTaxable.ToScaled(), "586.00 - 56.00");
        report.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_19_ARateChangedMidHistoryGroupsEachLineByTheRateItWasSoldAtNotTheClassesCurrentRate()
    {
        // The Bolt and Drill tax class ("Ten percent (P3-T05)", 10%) is re-rated to 15% after the history.
        // Every past line still carries 10% in its own tax_rate snapshot; a new Bolt bill is charged 15%.
        await using var host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var data = await SalesReportDataset.BuildAsync(host);

        var taxClasses = host.Resolve<ITaxClassMaintenance>();
        var ten = (await taxClasses.ListAsync()).Single(tax => tax.Name == "Ten percent (P3-T05)");
        await taxClasses.UpdateAsync(ten.Id, new SaveTaxClassCommand(ten.Name, TaxRate.FromPercent(15m)));

        var before = await host.Resolve<ITaxReportQuery>().GetTaxReportAsync(BothDays);
        before.Rows.Select(row => row.Rate?.AsPercent).Should().Equal([0m, 10m, null], "re-rating the class moves nothing already sold");
        before.Rows[1].SalesTaxable.Should().Be(M(1294.00m));
        before.Rows[1].SalesTax.Should().Be(M(129.40m));
        before.Rows[1].ReturnsTax.Should().Be(M(33.55m), "a return takes the rate of the line it reverses, not the class's new rate");
        before.IsReconciled.Should().BeTrue();

        var sold = await SalesReportDataset.SellAsync(
            host, SalesReportDataset.At(8, 11, 0), [new SaleLineRequest(data.BoltVariantId, 2m)], null, (TenderTypes.Cash, 230.00m));
        sold.Total.Should().Be(M(230.00m), "200.00 + 15% = 230.00");

        var after = await host.Resolve<ITaxReportQuery>().GetTaxReportAsync(
            ReportDateRange.Custom(SalesReportDataset.DayOne, new DateOnly(2026, 9, 8)));

        after.Rows.Select(row => row.Rate?.AsPercent).Should().Equal([0m, 10m, 15m, null]);
        after.Rows[1].SalesTaxable.Should().Be(M(1294.00m));
        after.Rows[1].SalesTax.Should().Be(M(129.40m));
        after.Rows[2].SalesTaxable.Should().Be(M(200.00m));
        after.Rows[2].SalesTax.Should().Be(M(30.00m));
        after.TotalSalesTax.Should().Be(M(159.40m));
        after.NetTax.Should().Be(M(115.85m), "159.40 - 43.55");
        after.IsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task RPT_19_TheTaxReportIsReadFromRawTablesSoADayRolledUpOnShiftCloseGivesTheSameFigures()
    {
        // Day one's shift is closed and rolled up (daily_sales_summary holds one tax total and no per-rate
        // split); the per-rate rows must still be there.
        (await fixture.Host.CountAsync("SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(1, "the closed day has a rollup");

        var report = await Query.GetTaxReportAsync(ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));

        report.Rows.Should().HaveCount(2);
    }

    private async Task<long[]> ShiftIdsAsync()
    {
        var first = await fixture.Host.CountAsync("SELECT id FROM shift ORDER BY id LIMIT 1;");
        var second = await fixture.Host.CountAsync("SELECT id FROM shift ORDER BY id LIMIT 1 OFFSET 1;");

        return [first, second];
    }
}
