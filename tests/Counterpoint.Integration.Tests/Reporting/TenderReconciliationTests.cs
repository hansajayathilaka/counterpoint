using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Exchanges;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// Tender reconciliation (task P3-T06 "Do this" #3; it is not SRS RPT-05, which is the Z report): the closed
/// shifts' Z tenders against the period's payments. Hand figures from <see cref="SalesReportDataset"/>:
/// </summary>
/// <remarks>
/// <code>
/// Shift 1 (Sep 6, closed 20:00):  CASH 400.00 + 250.00 = 650.00   CARD 143.40 + 220.00 = 363.40   (cancelled B4's 275.00 is nowhere)
/// Shift 2 (Sep 7, still open):    sales CASH 550.00 + 160.00 = 710.00
///                                 refunds CASH 94.05 (R1) + 275.00 (R2) = 369.05   CARD 110.00 (R3)
/// Sep 6-7 range side:  CASH 1360.00 - 369.05 = 990.95   CARD 363.40 - 110.00 = 253.40   total 1244.35
/// Sep 6-7 Z side:      CASH 650.00                     CARD 363.40                      total 1013.40
/// </code>
/// </remarks>
[Collection(StockCashReportFixture.Name)]
public sealed class TenderReconciliationTests(StockCashReportFixture fixture)
{
    private static readonly ReportDateRange BothDays = ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayTwo);

    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private ITenderReconciliationQuery Query => fixture.Host.Resolve<ITenderReconciliationQuery>();

    [Fact]
    public async Task TenderReconciliation_AFullyZdDayTiesOutExactlyTenderByTender()
    {
        var report = await Query.GetReconciliationAsync(ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));

        report.Shifts.Should().ContainSingle();
        report.Shifts[0].ShiftNo.Should().Be("SH-000001");
        report.Shifts[0].BusinessDate.Should().Be(SalesReportDataset.DayOne);
        report.Shifts[0].NetTotal.Should().Be(M(1013.40m));
        report.Shifts[0].Tenders.Select(t => (t.TenderType, t.SalesAmount, t.RefundsAmount, t.NetAmount)).Should().Equal(
            [("CARD", M(363.40m), Money.Zero, M(363.40m)), ("CASH", M(650.00m), Money.Zero, M(650.00m))]);

        report.ByTender.Select(row => row.TenderType).Should().Equal("CARD", "CASH");
        var card = report.ByTender[0];
        card.ZSales.Should().Be(M(363.40m));
        card.RangeSales.Should().Be(M(363.40m));
        card.Difference.Should().Be(Money.Zero);
        var cash = report.ByTender[1];
        cash.ZSales.Should().Be(M(650.00m));
        cash.ZNet.Should().Be(M(650.00m));
        cash.RangeNet.Should().Be(M(650.00m), "B4's cancelled 275.00 is in neither side");
        cash.Difference.Should().Be(Money.Zero);

        report.ZNetTotal.Should().Be(M(1013.40m));
        report.RangeNetTotal.Should().Be(M(1013.40m));
        report.Difference.Should().Be(Money.Zero);
        report.NotZd.Should().BeEmpty();
        report.IsTiedOut.Should().BeTrue();

        // The range side is an independent read of payment: completed sales' tenders by business date.
        (await fixture.Host.CountAsync(
            "SELECT SUM(p.amount) FROM payment p JOIN sale s ON s.id = p.sale_id WHERE s.status = 'COMPLETED' AND s.business_date = '2026-09-06';"))
            .Should().Be(report.RangeNetTotal.ToScaled());
    }

    [Fact]
    public async Task TenderReconciliation_ARangeWithAnOpenShiftFlagsItNotZdAndIsNotTiedOut()
    {
        var report = await Query.GetReconciliationAsync(BothDays);

        report.Shifts.Select(shift => shift.ShiftNo).Should().Equal("SH-000001");

        report.ByTender.Select(row => row.TenderType).Should().Equal("CARD", "CASH");

        var card = report.ByTender[0];
        card.ZSales.Should().Be(M(363.40m));
        card.ZRefunds.Should().Be(Money.Zero);
        card.ZNet.Should().Be(M(363.40m));
        card.RangeSales.Should().Be(M(363.40m));
        card.RangeRefunds.Should().Be(M(110.00m), "R3's card refund, a positive magnitude");
        card.RangeNet.Should().Be(M(253.40m));
        card.Difference.Should().Be(M(-110.00m), "range net 253.40 less Z net 363.40");

        var cash = report.ByTender[1];
        cash.ZSales.Should().Be(M(650.00m));
        cash.ZNet.Should().Be(M(650.00m));
        cash.RangeSales.Should().Be(M(1360.00m), "650.00 + 550.00 + 160.00");
        cash.RangeRefunds.Should().Be(M(369.05m), "R1 94.05 + R2 275.00");
        cash.RangeNet.Should().Be(M(990.95m));
        cash.Difference.Should().Be(M(340.95m));

        report.ZNetTotal.Should().Be(M(1013.40m));
        report.RangeNetTotal.Should().Be(M(1244.35m));
        report.Difference.Should().Be(M(230.95m));

        report.NotZd.Should().ContainSingle();
        var open = report.NotZd[0];
        open.ShiftNo.Should().Be("SH-000002");
        open.Status.Should().Be("OPEN");
        open.BusinessDate.Should().Be(SalesReportDataset.DayTwo);
        open.SalesInRange.Should().Be(2, "B5 and B6");
        open.ReturnsInRange.Should().Be(3, "R1, R2 and R3");
        open.Reason.Should().Contain("Not Z'd").And.Contain("open");

        report.IsTiedOut.Should().BeFalse("an open shift's trading is in the period but on no Z report");
    }

    [Fact]
    public async Task TenderReconciliation_ADayWhoseOnlyShiftIsOpenHasNoZSideAndEveryTenderIsADifference()
    {
        var report = await Query.GetReconciliationAsync(ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo));

        report.Shifts.Should().BeEmpty();
        report.ZNetTotal.Should().Be(Money.Zero);
        report.ByTender.Select(row => (row.TenderType, row.ZNet, row.RangeSales, row.RangeRefunds, row.Difference)).Should().Equal(
        [
            ("CARD", Money.Zero, Money.Zero, M(110.00m), M(-110.00m)),
            ("CASH", Money.Zero, M(710.00m), M(369.05m), M(340.95m)),
        ]);
        report.RangeNetTotal.Should().Be(M(230.95m));
        report.NotZd.Select(item => (item.ShiftNo, item.Status)).Should().Equal([("SH-000002", "OPEN")]);
        report.IsTiedOut.Should().BeFalse();
    }

    [Fact]
    public async Task TenderReconciliation_RefundsArePaidOutAsNegativePaymentsAndShownAsPositiveMagnitudesThatReduceTheNet()
    {
        // The ledger stores a refund as a negative payment; both sides of the tie-out show the magnitude.
        (await fixture.Host.CountAsync(
            "SELECT SUM(amount) FROM payment WHERE sale_return_id IS NOT NULL AND tender_type = 'CASH';"))
            .Should().Be(-3_690_500, "-(94.05 + 275.00) in scaled units");

        var report = await Query.GetReconciliationAsync(BothDays);
        var cash = report.ByTender.Single(row => row.TenderType == "CASH");

        cash.RangeRefunds.ToScaled().Should().Be(3_690_500);
        cash.RangeNet.Should().Be(cash.RangeSales - cash.RangeRefunds);
    }

    [Fact]
    public async Task AC_12_TheTenderTotalEqualsTheSalesSummarysTenderTotalAndNetSalesPlusNetTax()
    {
        // AC-12's identity for a Z'd range: what the till took, net of refunds (1244.35), is the sales summary's tender
        // total, and is the tax report's net taxable value plus its net tax: 1158.50 + 85.85. The dataset has no bill rounding.
        var reconciliation = await Query.GetReconciliationAsync(BothDays);
        var summary = await fixture.Host.Resolve<ISalesSummaryReportQuery>().GetSummaryAsync(BothDays);
        var tax = await fixture.Host.Resolve<ITaxReportQuery>().GetTaxReportAsync(BothDays);

        reconciliation.RangeNetTotal.Should().Be(M(1244.35m));
        summary.Totals.TenderTotal.Should().Be(reconciliation.RangeNetTotal, "the by-tender table and the headline read the same payments");
        (tax.TotalNetTaxable + tax.NetTax).Should().Be(reconciliation.RangeNetTotal, "1158.50 + 85.85");
        summary.ByTender.Select(row => (row.TenderType, row.NetAmount)).Should().Equal(
            reconciliation.ByTender.Select(row => (row.TenderType, row.RangeNet)));
    }

    [Fact]
    public async Task TenderReconciliation_ARangeWithNoTradingIsVacuouslyTiedOutAndACancelledBillOnALaterDayDoesNotCount()
    {
        // Sep 8 - 30 holds only the Hinge bill that was rung up and cancelled on Sep 11.
        var report = await Query.GetReconciliationAsync(ReportDateRange.Custom(new(2026, 9, 8), new(2026, 9, 30)));

        report.Shifts.Should().BeEmpty();
        report.ByTender.Should().BeEmpty("a cancelled bill's payments are on neither side");
        report.NotZd.Should().BeEmpty("the open shift is dated Sep 7 and holds no completed trading in this range");
        report.Difference.Should().Be(Money.Zero);
        report.IsTiedOut.Should().BeTrue();
    }

    [Fact]
    public async Task TenderReconciliation_AnOpenShiftWithNoTradingAtAllIsStillListedAndTheDayIsNotTiedOut()
    {
        await using var host = await SaleFixture.CreateSignedInAsync();

        var report = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(
            ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));

        report.Shifts.Should().BeEmpty();
        report.ByTender.Should().BeEmpty();
        report.Difference.Should().Be(Money.Zero, "no money moved, so there is no difference to explain");
        report.NotZd.Should().ContainSingle();
        report.NotZd[0].ShiftNo.Should().Be("SH-000001");
        report.NotZd[0].Status.Should().Be("OPEN");
        report.NotZd[0].SalesInRange.Should().Be(0);
        report.NotZd[0].ReturnsInRange.Should().Be(0);
        report.IsTiedOut.Should().BeFalse("the day's Z report has not been run, whatever the arithmetic says");
    }

    [Fact]
    public async Task TenderReconciliation_OnceEveryShiftIsClosedTheWholeRangeTiesOutAndTheZSideIsTheZReportsOwnFigures()
    {
        await using var host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        var data = await SalesReportDataset.BuildAsync(host);
        var session = host.Resolve<ISession>();
        var shiftTwo = session.ShiftId!.Value;

        // Shift 2's drawer should hold 0.00 float + 710.00 cash sales - 369.05 cash refunds = 340.95.
        var closed = await host.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftTwo, data.OwnerId, M(340.95m), SalesReportDataset.At(7, 21, 0), Note: "P3-T06 fixture"));
        closed.Report.ExpectedCash.Should().Be(M(340.95m));
        closed.Report.Variance.Should().Be(Money.Zero);

        var report = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(BothDays);

        report.Shifts.Select(shift => shift.ShiftNo).Should().Equal("SH-000001", "SH-000002");
        report.NotZd.Should().BeEmpty();

        report.ByTender.Select(row => (row.TenderType, row.ZSales, row.ZRefunds, row.ZNet, row.RangeNet, row.Difference)).Should().Equal(
        [
            ("CARD", M(363.40m), M(110.00m), M(253.40m), M(253.40m), Money.Zero),
            ("CASH", M(1360.00m), M(369.05m), M(990.95m), M(990.95m), Money.Zero),
        ]);
        report.ZNetTotal.Should().Be(M(1244.35m));
        report.RangeNetTotal.Should().Be(M(1244.35m));
        report.Difference.Should().Be(Money.Zero);
        report.IsTiedOut.Should().BeTrue();

        // The Z side is the shared ShiftTenderBreakdown: shift 2's tenders equal what its own Z report printed
        // at close and what the X report says of it.
        var secondShift = report.Shifts[1];
        secondShift.Tenders.Should().BeEquivalentTo(closed.Report.Tenders);
        secondShift.NetTotal.Should().Be(M(230.95m), "CASH 340.95 + CARD -110.00");

        var x = await host.Resolve<IXReportService>().GenerateAsync(shiftTwo);
        x.Tenders.Should().BeEquivalentTo(secondShift.Tenders, "the X report and the reconciliation read the same shared SQL");
        x.Tenders.Select(t => (t.TenderType, t.SalesAmount, t.RefundsAmount, t.NetAmount)).Should().Equal(
            [("CARD", Money.Zero, M(110.00m), M(-110.00m)), ("CASH", M(710.00m), M(369.05m), M(340.95m))]);

        var xOne = await host.Resolve<IXReportService>().GenerateAsync(report.Shifts[0].ShiftId);
        xOne.Tenders.Should().BeEquivalentTo(report.Shifts[0].Tenders);
    }

    [Fact]
    public async Task TenderReconciliation_EveryTenderTypeAppearsOnItsOwnRowNotFoldedIntoThreeBuckets()
    {
        await using var host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SalesReportDataset.SeedReturnNumberSequenceAsync(host);
        await SalesReportDataset.DisableBackupOnShiftCloseAsync(host);
        await host.Resolve<Counterpoint.Application.Abstractions.Persistence.INumberSequenceConfiguration>()
            .ConfigureAsync("CREDIT_NOTE", "CN-", "{prefix}{yyyy}-{n:000000}", 1);

        var session = host.Resolve<ISession>();
        var userId = session.CurrentUser!.Id;
        var shiftId = session.ShiftId!.Value;
        var item = await PricedVariantSeeder.SeedAsync(host, "TT-A", 100.00m, 0m);
        var uom = await host.CountAsync("SELECT id FROM uom ORDER BY id LIMIT 1;");

        // Nothing here is taxed and every bill is exactly 100.00.
        await SalesReportDataset.SellAsync(host, SalesReportDataset.At(6, 10, 0), [new SaleLineRequest(item, 1m)], null, (TenderTypes.Cash, 100.00m));
        await host.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, SalesReportDataset.At(6, 11, 0), [new SaleLineRequest(item, 1m)],
            [new TenderRequest(TenderTypes.BankTransfer, M(100.00m), "TRF-1")]));
        await host.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, SalesReportDataset.At(6, 12, 0), [new SaleLineRequest(item, 1m)],
            [new TenderRequest(TenderTypes.Cheque, M(100.00m), "CHQ-7")]));
        await SalesReportDataset.SellAsync(
            host, SalesReportDataset.At(6, 13, 0), [new SaleLineRequest(item, 1m)], null, (TenderTypes.Card, 40.00m), (TenderTypes.Cash, 60.00m));

        // A cash sale returned as store credit, and the credit spent on an open-item bill.
        var kept = await SalesReportDataset.SellAsync(host, SalesReportDataset.At(6, 14, 0), [new SaleLineRequest(item, 1m)], null, (TenderTypes.Cash, 100.00m));
        var keptLine = await host.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + kept.SaleId + ";");
        var credit = await host.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            kept.SaleId, userId, shiftId, SalesReportDataset.At(6, 15, 0),
            [new ReturnLineRequest(keptLine, Quantity.FromDecimal(1m, keptLine), ReturnDisposition.Sellable, "Store credit")],
            RefundMethod.CreditNote));
        await host.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId, shiftId, SalesReportDataset.At(6, 16, 0),
            [new SaleLineRequest(null, 1m, uom, "Store credit redemption", M(100.00m))],
            [new TenderRequest(TenderTypes.CreditNote, M(100.00m), credit.CreditNoteNumber)]));

        // An exchange: one unit back, one unit out, the 100.00 credit settled as EXCHANGE on both documents.
        var swapped = await SalesReportDataset.SellAsync(host, SalesReportDataset.At(6, 17, 0), [new SaleLineRequest(item, 1m)], null, (TenderTypes.Cash, 100.00m));
        var swappedLine = await host.CountAsync("SELECT id FROM sale_line WHERE sale_id = " + swapped.SaleId + ";");
        await host.Resolve<ICreateExchange>().CreateAsync(new CreateExchangeCommand(
            swapped.SaleId, userId, shiftId, SalesReportDataset.At(6, 18, 0),
            [new ReturnLineRequest(swappedLine, Quantity.FromDecimal(1m, swappedLine), ReturnDisposition.Sellable, "Wrong colour")],
            [new SaleLineRequest(item, 1m)],
            DifferenceTenders: []));

        // Cash in the drawer: 100 + 60 + 100 + 100 = 360.00 (the credit-note refund and redemption are not cash).
        var closed = await host.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, userId, M(360.00m), SalesReportDataset.At(6, 20, 0), Note: "P3-T06 fixture"));
        closed.Report.Variance.Should().Be(Money.Zero, "the hand-worked drawer is 360.00");

        var report = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(
            ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));

        report.ByTender.Select(row => row.TenderType).Should().Equal(
            ["BANK_TRANSFER", "CARD", "CASH", "CHEQUE", "CREDIT_NOTE", "EXCHANGE"], "six tender types, six rows");

        report.ByTender.Select(row => (row.TenderType, row.ZSales, row.ZRefunds, row.RangeSales, row.RangeRefunds, row.Difference)).Should().Equal(
        [
            ("BANK_TRANSFER", M(100.00m), Money.Zero, M(100.00m), Money.Zero, Money.Zero),
            ("CARD", M(40.00m), Money.Zero, M(40.00m), Money.Zero, Money.Zero),
            ("CASH", M(360.00m), Money.Zero, M(360.00m), Money.Zero, Money.Zero),
            ("CHEQUE", M(100.00m), Money.Zero, M(100.00m), Money.Zero, Money.Zero),
            ("CREDIT_NOTE", M(100.00m), M(100.00m), M(100.00m), M(100.00m), Money.Zero),
            ("EXCHANGE", M(100.00m), M(100.00m), M(100.00m), M(100.00m), Money.Zero),
        ]);

        report.ZNetTotal.Should().Be(M(600.00m), "100 + 40 + 360 + 100 + 0 + 0");
        report.RangeNetTotal.Should().Be(M(600.00m));
        report.Difference.Should().Be(Money.Zero);
        report.NotZd.Should().BeEmpty();
        report.IsTiedOut.Should().BeTrue();

        (await host.CountAsync("SELECT SUM(amount) FROM payment;")).Should().Be(6_000_000, "independent total of every payment row: 800.00 taken less 200.00 paid back");
    }

    [Fact]
    public async Task TenderReconciliation_AShiftClosedUnderAnEarlierBusinessDateThatTradedPastMidnightIsFlaggedWhenOnlyItsLaterDayIsAsked()
    {
        // The seeded shift opened on Sep 6; a bill is rung up in it at 00:30 on Sep 7 (business date Sep 7) and
        // the shift is closed at 01:00. Its Z report is dated Sep 6.
        await using var host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SalesReportDataset.DisableBackupOnShiftCloseAsync(host);
        var session = host.Resolve<ISession>();
        var item = await PricedVariantSeeder.SeedAsync(host, "MID-A", 100.00m, 0m);

        await SalesReportDataset.SellAsync(host, SalesReportDataset.At(7, 0, 30), [new SaleLineRequest(item, 1m)], null, (TenderTypes.Cash, 100.00m));
        await host.Resolve<ICloseShift>().CloseAsync(new CloseShiftCommand(
            session.ShiftId!.Value, session.CurrentUser!.Id, M(100.00m), SalesReportDataset.At(7, 1, 0), Note: "closed after midnight"));

        var later = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(
            ReportDateRange.Custom(SalesReportDataset.DayTwo, SalesReportDataset.DayTwo));

        later.Shifts.Should().BeEmpty("the shift's Z report is dated Sep 6");
        later.ByTender.Select(row => (row.TenderType, row.ZNet, row.RangeNet, row.Difference)).Should().Equal(
            [("CASH", Money.Zero, M(100.00m), M(100.00m))]);
        later.NotZd.Should().ContainSingle();
        later.NotZd[0].Status.Should().Be("CLOSED");
        later.NotZd[0].BusinessDate.Should().Be(SalesReportDataset.DayOne);
        later.NotZd[0].SalesInRange.Should().Be(1);
        later.NotZd[0].Reason.Should().Contain("2026-09-06").And.Contain("outside this range");
        later.IsTiedOut.Should().BeFalse();

        var both = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(BothDays);
        both.Shifts.Should().ContainSingle();
        both.NotZd.Should().BeEmpty();
        both.Difference.Should().Be(Money.Zero);
        both.IsTiedOut.Should().BeTrue("asking for both days puts the whole shift inside the range");

        // The shift's Z report (dated Sep 6) holds the Sep 7 bill, so Sep 6 alone shows the difference the other
        // way round: the Z side is per shift, the range side is per document date.
        var earlier = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(
            ReportDateRange.Custom(SalesReportDataset.DayOne, SalesReportDataset.DayOne));
        earlier.ByTender.Select(row => (row.TenderType, row.ZNet, row.RangeNet, row.Difference)).Should().Equal(
            [("CASH", M(100.00m), Money.Zero, M(-100.00m))]);

        // The -100.00 is named, not left to look like a defect: the shift's Z report is dated Sep 6 but it also holds
        // the Sep 7 bill, which is outside this range.
        earlier.NotZd.Should().ContainSingle();
        var spilled = earlier.NotZd[0];
        spilled.ShiftNo.Should().Be("SH-000001");
        spilled.Status.Should().Be("CLOSED");
        spilled.BusinessDate.Should().Be(SalesReportDataset.DayOne);
        spilled.IsDateBoundary.Should().BeTrue();
        spilled.SalesOutsideRange.Should().Be(1);
        spilled.ReturnsOutsideRange.Should().Be(0);
        spilled.SalesInRange.Should().Be(0);
        spilled.ReturnsInRange.Should().Be(0);
        spilled.NetEffect.Should().Be(M(-100.00m), "the Z side has 100.00 the payments side does not");
        spilled.Reason.Should().Contain("2026-09-06").And.Contain("2026-09-07").And.Contain("outside this range");
        earlier.ByTender.Single().Unexplained.Should().Be(Money.Zero);
        earlier.HasUnexplainedDifference.Should().BeFalse();
        earlier.IsTiedOut.Should().BeFalse("a listed item always keeps the report from claiming a tie-out");
    }

    private static DateTimeOffset At(int month, int day, int hour, int minute) =>
        new(2026, month, day, hour, minute, 0, SalesReportDataset.ShopOffset);

    /// <summary>
    /// SH-000001 (Sep 6, empty) is closed. SH-000002 opens Sep 30 and is closed at 01:00 on Oct 1:
    /// Sep 30 20:00 a bill of 2 x 100.00 paid CASH 200.00; Oct 1 00:30 a bill of 3 x 100.00 paid CARD 300.00;
    /// Oct 1 00:40 one unit of the first bill returned, refunded CASH 100.00. The shift's Z report is dated
    /// Sep 30 and holds CASH 100.00 net and CARD 300.00 net.
    /// </summary>
    private static async Task<SaleFixture> BuildMonthBoundaryAsync()
    {
        var host = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SalesReportDataset.DisableBackupOnShiftCloseAsync(host);
        await SalesReportDataset.SeedReturnNumberSequenceAsync(host);

        var session = host.Resolve<ISession>();
        var userId = session.CurrentUser!.Id;
        var item = await PricedVariantSeeder.SeedAsync(host, "MON-A", 100.00m, 0m);

        await host.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(session.ShiftId!.Value, userId, Money.Zero, At(9, 6, 20, 0)));
        await host.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(userId, Money.Zero, At(9, 30, 9, 0)));
        var shiftId = session.ShiftId!.Value;

        var first = await SalesReportDataset.SellAsync(host, At(9, 30, 20, 0), [new SaleLineRequest(item, 2m)], null, (TenderTypes.Cash, 200.00m));
        await SalesReportDataset.SellAsync(host, At(10, 1, 0, 30), [new SaleLineRequest(item, 3m)], null, (TenderTypes.Card, 300.00m));
        await SalesReportDataset.ReturnAsync(host, first, 1, 1m, ReturnDisposition.Sellable, "Changed mind", At(10, 1, 0, 40), shiftId, userId);

        await host.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, userId, M(100.00m), At(10, 1, 1, 0), Note: "closed after midnight"));

        return host;
    }

    [Fact]
    public async Task TenderReconciliation_AMonthEndingOnTheDateAShiftOpenedNamesTheTradingThatSpilledIntoTheNextMonth()
    {
        await using var host = await BuildMonthBoundaryAsync();

        var september = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(
            ReportDateRange.Custom(new(2026, 9, 1), new(2026, 9, 30)));

        september.Shifts.Select(shift => shift.ShiftNo).Should().Equal("SH-000001", "SH-000002");
        september.ByTender.Select(row => (row.TenderType, row.ZNet, row.RangeNet, row.Difference, row.Unexplained)).Should().Equal(
        [
            ("CARD", M(300.00m), Money.Zero, M(-300.00m), Money.Zero),
            ("CASH", M(100.00m), M(200.00m), M(100.00m), Money.Zero),
        ]);
        september.Difference.Should().Be(M(-200.00m));

        september.NotZd.Should().ContainSingle();
        var item = september.NotZd[0];
        item.ShiftNo.Should().Be("SH-000002");
        item.Status.Should().Be("CLOSED");
        item.BusinessDate.Should().Be(new DateOnly(2026, 9, 30));
        item.IsDateBoundary.Should().BeTrue();
        item.SalesOutsideRange.Should().Be(1, "the Oct 1 00:30 card bill");
        item.ReturnsOutsideRange.Should().Be(1, "the Oct 1 00:40 refund");
        item.SalesInRange.Should().Be(0);
        item.ReturnsInRange.Should().Be(0);
        item.NetEffect.Should().Be(M(-200.00m), "CASH +100.00 (refund only the Z side has) and CARD -300.00");
        item.Reason.Should().Contain("2026-09-30").And.Contain("2026-10-01").And.Contain("outside this range");

        september.NotZd.Aggregate(Money.Zero, (sum, listed) => sum + listed.NetEffect).Should().Be(september.Difference);
        september.HasUnexplainedDifference.Should().BeFalse("every non-zero difference is accounted for by the listed shift");
        september.IsTiedOut.Should().BeFalse();
    }

    [Fact]
    public async Task TenderReconciliation_AMonthStartingTheDayAfterAShiftOpenedNamesItsTradingThatIsOnAnEarlierZReport()
    {
        await using var host = await BuildMonthBoundaryAsync();

        var october = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(
            ReportDateRange.Custom(new(2026, 10, 1), new(2026, 10, 31)));

        october.Shifts.Should().BeEmpty("the shift's Z report is dated Sep 30");
        october.ByTender.Select(row => (row.TenderType, row.ZNet, row.RangeNet, row.Difference, row.Unexplained)).Should().Equal(
        [
            ("CARD", Money.Zero, M(300.00m), M(300.00m), Money.Zero),
            ("CASH", Money.Zero, M(-100.00m), M(-100.00m), Money.Zero),
        ]);
        october.Difference.Should().Be(M(200.00m));

        october.NotZd.Should().ContainSingle();
        var item = october.NotZd[0];
        item.ShiftNo.Should().Be("SH-000002");
        item.Status.Should().Be("CLOSED");
        item.BusinessDate.Should().Be(new DateOnly(2026, 9, 30));
        item.IsDateBoundary.Should().BeFalse();
        item.SalesInRange.Should().Be(1);
        item.ReturnsInRange.Should().Be(1);
        item.NetEffect.Should().Be(M(200.00m));
        item.Reason.Should().Contain("2026-09-30").And.Contain("outside this range");

        october.NotZd.Aggregate(Money.Zero, (sum, listed) => sum + listed.NetEffect).Should().Be(october.Difference);
        october.HasUnexplainedDifference.Should().BeFalse();
        october.IsTiedOut.Should().BeFalse();
    }

    [Fact]
    public async Task TenderReconciliation_ARangeThatTakesInTheWholeShiftOnEitherSideOfAMonthBoundaryTiesOut()
    {
        await using var host = await BuildMonthBoundaryAsync();

        foreach (var range in new[]
        {
            ReportDateRange.Custom(new(2026, 9, 1), new(2026, 10, 31)),
            ReportDateRange.Custom(new(2026, 9, 30), new(2026, 10, 1)),
        })
        {
            var report = await host.Resolve<ITenderReconciliationQuery>().GetReconciliationAsync(range);

            report.NotZd.Should().BeEmpty();
            report.ByTender.Should().OnlyContain(row => row.Difference == Money.Zero && row.Unexplained == Money.Zero);
            report.ZNetTotal.Should().Be(M(400.00m), "CASH 200.00 - 100.00 + CARD 300.00");
            report.IsTiedOut.Should().BeTrue();
        }
    }
}
