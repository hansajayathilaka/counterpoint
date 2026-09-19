using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Reporting.Queries;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// The shared report query layer (task P3-T04, SRS FR-9.1-FR-9.6, NFR-P5): the canonical figures,
/// the routing that reads rollups for closed dates and raw tables around the open shift, and the
/// owner-only projection.
/// </summary>
/// <remarks>
/// Every fixture that closes a shift is built with <c>includeBackup: true</c> because
/// <c>CloseShiftHandler</c> needs <c>IShiftCloseBackupTrigger</c>, which <c>SaleFixture</c> wires
/// only when that flag is passed - the same reason every test in <c>CloseShiftHandlerTests</c> asks
/// for it. <c>backup.on_shift_close</c> is then switched off, the way
/// <c>CloseShiftHandlerTests.FR_11_1_ABackupIsSkippedRatherThanAttemptedWhenDisabledOnShiftClose</c>
/// switches it off: these tests are about the report layer, and a shift close that also reaches
/// into the backup machinery would couple them to the filesystem (and to whatever passphrase the
/// machine's protected store happens to hold) for no benefit.
/// </remarks>
public sealed class ReportQueryLayerTests
{
    private const decimal TaxPercent = 10m;
    private const decimal UnitPrice = 100.00m;
    private const decimal UnitCost = 60.00m;

    // The seeded shift opens on 2026-09-06 (SaleFixture's FixedTimeProvider starts there), and a
    // sale's business_date is the date of its sold_at.
    private static readonly DateTimeOffset DayOneSoldAt = new(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayOneReturnedAt = new(2026, 9, 6, 11, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayOneClosedAt = new(2026, 9, 6, 20, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayTwoSoldAt = new(2026, 9, 10, 9, 30, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayTwoClosedAt = new(2026, 9, 10, 20, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayTwoCancelledAt = new(2026, 9, 10, 21, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayOneCancelledAt = new(2026, 9, 6, 21, 0, 0, TimeSpan.FromHours(5.5));

    // A date between DayOne and DayTwo, used by the range-that-ends-before-the-open-shift test, and a
    // date past DayTwo where an open shift can sit so the split key lands beyond a range's own end.
    private static readonly DateTimeOffset DayBetweenSoldAt = new(2026, 9, 8, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayBetweenClosedAt = new(2026, 9, 8, 20, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset DayOpenTodayOpenedAt = new(2026, 9, 12, 9, 0, 0, TimeSpan.FromHours(5.5));

    private static readonly DateOnly DayOne = new(2026, 9, 6);
    private static readonly DateOnly DayBetween = new(2026, 9, 8);
    private static readonly DateOnly DayTwo = new(2026, 9, 10);

    // ---- Canonical figures, hand-worked --------------------------------------------------------

    [Fact]
    public async Task TheCanonicalFiguresMatchAHandWorkedTradingDay()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SeedReturnNumberSequenceAsync(fixture);
        await DisableBackupOnShiftCloseAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // Sale: 3 pieces @ 100.00, 10% tax -> subtotal 300.00, tax 30.00, total 330.00, cogs 180.00.
        var sale = await CompleteAsync(fixture, variantId, quantity: 3m, DayOneSoldAt);
        sale.Total.Should().Be(Money.FromDecimal(330.00m), "the hand-worked figures below depend on it");

        // Return 1 piece -> subtotal 100.00, tax 10.00, refund 110.00, return cogs 60.00.
        var returned = await ReturnOneAsync(fixture, sale.SaleId, shiftId, user.Id);
        returned.TotalRefund.Should().Be(Money.FromDecimal(110.00m));

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, sale.Total, DayOneClosedAt));

        var summary = await fixture.Resolve<ISalesPeriodSummaryQuery>()
            .GetSalesSummaryAsync(ReportDateRange.Custom(DayOne, DayOne));

        summary.BillCount.Should().Be(1);
        summary.ReturnCount.Should().Be(1);
        summary.GrossSales.Should().Be(Money.FromDecimal(300.00m), "gross is subtotal + line_discount, before tax");
        summary.Discounts.Should().Be(Money.Zero);
        summary.Tax.Should().Be(Money.FromDecimal(30.00m));
        summary.NetSales.Should().Be(Money.FromDecimal(200.00m), "300.00 gross - 0.00 discount - 100.00 return subtotal");
        summary.ReturnsValue.Should().Be(Money.FromDecimal(110.00m));
        summary.TenderTotal.Should().Be(Money.FromDecimal(220.00m), "330.00 tendered - 110.00 refunded");

        var profit = await fixture.Resolve<IProfitPeriodSummaryQuery>()
            .GetProfitSummaryAsync(ReportDateRange.Custom(DayOne, DayOne));

        profit.NetSales.Should().Be(summary.NetSales);
        profit.Cogs.Should().Be(Money.FromDecimal(120.00m), "3 x 60.00 sold - 1 x 60.00 returned");
        profit.GrossProfit.Should().Be(Money.FromDecimal(80.00m), "200.00 net - 120.00 cogs");
        profit.MarginRate.Should().Be(0.4m, "80.00 / 200.00");
    }

    // ---- Routing: a range spanning a closed date and the open shift ----------------------------

    [Fact]
    public async Task ASpanningClosedAndOpenRangeMatchesRawOnly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await SeedReturnNumberSequenceAsync(fixture);
        await DisableBackupOnShiftCloseAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var firstShiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        var summaryQuery = fixture.Resolve<ISalesPeriodSummaryQuery>();
        var profitQuery = fixture.Resolve<IProfitPeriodSummaryQuery>();

        // Day one: 2 pieces sold and 1 returned, then the shift is closed so the date is rolled up.
        var dayOneSale = await CompleteAsync(fixture, variantId, quantity: 2m, DayOneSoldAt);
        await ReturnOneAsync(fixture, dayOneSale.SaleId, firstShiftId, user.Id);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(firstShiftId, user.Id, Money.Zero, DayOneClosedAt));

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(1, "the close wrote day one's rollup");

        // Day two: a fresh shift, still open when the report runs.
        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(user.Id, Money.Zero, DayTwoSoldAt));
        var dayTwoSale = await CompleteAsync(fixture, variantId, quantity: 3m, DayTwoSoldAt);
        dayTwoSale.Total.Should().Be(Money.FromDecimal(330.00m));

        var range = ReportDateRange.Custom(DayOne, DayTwo);

        var routed = await summaryQuery.GetSalesSummaryAsync(range);
        var raw = await summaryQuery.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routed.Should().Be(raw, "a range spanning a closed day and the open shift must equal the same range read from raw tables only");
        routed.BillCount.Should().Be(2);
        routed.ReturnCount.Should().Be(1);
        routed.GrossSales.Should().Be(Money.FromDecimal(500.00m));
        routed.Tax.Should().Be(Money.FromDecimal(50.00m));
        routed.NetSales.Should().Be(Money.FromDecimal(400.00m), "(200.00 - 100.00) closed + 300.00 open");
        routed.ReturnsValue.Should().Be(Money.FromDecimal(110.00m));
        routed.TenderTotal.Should().Be(Money.FromDecimal(440.00m), "(220.00 - 110.00) + 330.00");

        var routedProfit = await profitQuery.GetProfitSummaryAsync(range);
        var rawProfit = await profitQuery.GetProfitSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routedProfit.Should().Be(rawProfit, "cost must reconcile across the rollup/raw boundary too");
        routedProfit.Cogs.Should().Be(Money.FromDecimal(240.00m), "2 x 60.00 - 1 x 60.00 closed, plus 3 x 60.00 open");
        routedProfit.GrossProfit.Should().Be(Money.FromDecimal(160.00m), "400.00 net - 240.00 cogs");
        routedProfit.MarginRate.Should().Be(0.4m);
    }

    /// <summary>
    /// Task P3-T04's own named risk: "Rollup/raw union double-counting at the boundary." A single
    /// business date can hold a closed morning shift and an open afternoon shift. The rollup written
    /// when the morning shift closed covers the whole date so far, so reading it *and* the afternoon
    /// shift's raw sales would count the morning twice. The routing reads the open shift's date from
    /// the raw tables in full and excludes that date's rollup, which this proves.
    /// </summary>
    [Fact]
    public async Task ASameBusinessDateClosedMorningAndOpenAfternoonShiftIsNotDoubleCounted()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await DisableBackupOnShiftCloseAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var morningShiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // Morning: 2 pieces @ 100.00 -> subtotal 200.00, tax 20.00. Close -> rollup for DayOne.
        await CompleteAsync(fixture, variantId, quantity: 2m, DayOneSoldAt);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(morningShiftId, user.Id, Money.Zero, DayOneClosedAt));

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(1, "the morning close wrote a rollup row for the date");

        // Afternoon: a second shift the same business date, still open. 3 pieces -> subtotal 300.00.
        var afternoonOpenedAt = DayOneClosedAt.AddMinutes(30);
        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(user.Id, Money.Zero, afternoonOpenedAt));
        await CompleteAsync(fixture, variantId, quantity: 3m, afternoonOpenedAt.AddMinutes(30));

        var range = ReportDateRange.Custom(DayOne, DayOne);

        var query = fixture.Resolve<ISalesPeriodSummaryQuery>();
        var routed = await query.GetSalesSummaryAsync(range);
        var raw = await query.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routed.Should().Be(raw, "the open afternoon shift's date is read from raw in full");
        routed.BillCount.Should().Be(2);
        routed.GrossSales.Should().Be(Money.FromDecimal(500.00m));

        routed.GrossSales.Should().NotBe(
            Money.FromDecimal(700.00m),
            "700.00 would be the morning rollup plus the whole day's raw sales - the double-count this routing exists to avoid");
    }

    /// <summary>
    /// A closed date with no rollup row must be read from the raw tables, not silently read as zero.
    /// This is what makes the routed figures reconcile with raw data on a database whose rollups are
    /// incomplete - a date no Z report has rebuilt yet, or a tooling-seeded database.
    /// </summary>
    [Fact]
    public async Task AClosedDateWithoutARollupRowFallsBackToRawTables()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        var variantId = await SeedTaxedVariantAsync(fixture);
        await CompleteAsync(fixture, variantId, quantity: 2m, DayOneSoldAt);

        // Close the shift without going through the Z-report handler, so no rollup row is written -
        // the same raw repair-session UPDATE CloseShiftHandlerTests uses to reach a closed shift.
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(0, "the rollup must genuinely be absent for this test to mean anything");

        var range = ReportDateRange.Custom(DayOne, DayOne);

        var query = fixture.Resolve<ISalesPeriodSummaryQuery>();
        var routed = await query.GetSalesSummaryAsync(range);
        var raw = await query.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routed.Should().Be(raw);
        routed.BillCount.Should().Be(1);
        routed.NetSales.Should().Be(Money.FromDecimal(200.00m), "the raw sale, not zero");
    }

    [Fact]
    public async Task TheRawTablesRequiredPolicyReturnsTheSameFiguresAsTheRoutedDefault()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await DisableBackupOnShiftCloseAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        await CompleteAsync(fixture, variantId, quantity: 2m, DayOneSoldAt);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, Money.Zero, DayOneClosedAt));

        // A rollup row genuinely exists here, so this is the opt-out being compared to the rollup path.
        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(1);

        var range = ReportDateRange.Custom(DayOne, DayOne);

        var query = fixture.Resolve<ISalesPeriodSummaryQuery>();
        var routed = await query.GetSalesSummaryAsync(range);
        var raw = await query.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        raw.Should().Be(routed, "RawTablesRequired is a performance switch, never a correctness switch");
    }

    /// <summary>
    /// A range that ends <em>before</em> the open shift's business date must not read rollups past its
    /// own <c>To</c>. The rollup segment's upper bound is the range's own end, not the split key:
    /// while a shift is open "today", "last month" - or any past custom range - ends well before the
    /// split key, and without the explicit upper bound every rolled-up day between <c>To + 1</c> and
    /// <c>SplitKey - 1</c> would be added to the report, showing the owner weeks of trading they did
    /// not ask for.
    /// </summary>
    /// <remarks>
    /// This is the fixture every other routing test missed: the spanning test ends at the open
    /// shift's own date, the same-date double-count test ends on it too, and the no-rollup test has
    /// no open shift at all (so <c>SplitKey = To + 1</c> and the missing bound never bit).
    /// </remarks>
    [Fact]
    public async Task ARangeEndingBeforeTheOpenShiftDateIgnoresLaterRollups()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await DisableBackupOnShiftCloseAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // 2026-09-06: 2 @ 100.00 -> gross 200.00. Close -> rollup for the date.
        await CompleteAsync(fixture, variantId, quantity: 2m, DayOneSoldAt);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(fixture.Resolve<ISession>().ShiftId!.Value, user.Id, Money.Zero, DayOneClosedAt));

        // 2026-09-08: 3 @ 100.00 -> gross 300.00. Close -> rollup. Inside the range queried below.
        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(user.Id, Money.Zero, DayBetweenSoldAt));
        await CompleteAsync(fixture, variantId, quantity: 3m, DayBetweenSoldAt);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(fixture.Resolve<ISession>().ShiftId!.Value, user.Id, Money.Zero, DayBetweenClosedAt));

        // 2026-09-10: 4 @ 100.00 -> gross 400.00. Close -> rollup. This date is AFTER the range's To.
        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(user.Id, Money.Zero, DayTwoSoldAt));
        await CompleteAsync(fixture, variantId, quantity: 4m, DayTwoSoldAt);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(fixture.Resolve<ISession>().ShiftId!.Value, user.Id, Money.Zero, DayTwoClosedAt));

        // A shift open on 2026-09-12 - the split key, past the range's To. It opens nothing else.
        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(user.Id, Money.Zero, DayOpenTodayOpenedAt));

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary "
            + "WHERE business_date IN ('2026-09-06', '2026-09-08', '2026-09-10');"))
            .Should().Be(3, "three closed days are rolled up, one of them past the range's own end");

        var range = ReportDateRange.Custom(DayOne, DayBetween);

        var query = fixture.Resolve<ISalesPeriodSummaryQuery>();
        var routed = await query.GetSalesSummaryAsync(range);
        var raw = await query.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routed.Should().Be(raw, "a range ending before the open shift's date must read only the rollups it spans");
        routed.BillCount.Should().Be(2, "the 2026-09-10 rollup is past the range's end and must not leak in");
        routed.GrossSales.Should().Be(Money.FromDecimal(500.00m), "200.00 + 300.00, and never the 2026-09-10 day");
        routed.Tax.Should().Be(Money.FromDecimal(50.00m));
        routed.NetSales.Should().Be(Money.FromDecimal(500.00m));
        routed.TenderTotal.Should().Be(Money.FromDecimal(550.00m));

        // The bug this guards: 900.00 was the gross when the rollup segment ran past the range's end.
        routed.GrossSales.Should().NotBe(
            Money.FromDecimal(900.00m),
            "900.00 would be the three rolled-up days including the one after the range - the over-count this bound exists to stop");
    }

    /// <summary>
    /// A completed bill cancelled <em>after</em> its shift closed leaves a stale rollup row behind:
    /// <c>CancelSaleHandler</c> only requires the cancellation to fall on the sale's own business
    /// date, never that a shift is still open, and nothing rebuilds <c>daily_sales_summary</c> on
    /// cancellation. The rollup for that date would keep counting the cancelled bill while the raw
    /// tables do not, so the report layer reads a date holding any non-<c>COMPLETED</c> sale from raw.
    /// </summary>
    [Fact]
    public async Task ACancelledBillOnARolledUpDateIsNotCountedFromTheRollup()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await DisableBackupOnShiftCloseAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // 2 @ 100.00 -> subtotal 200.00, tax 20.00, total 220.00. Close -> rollup for 2026-09-06.
        var sale = await CompleteAsync(fixture, variantId, quantity: 2m, DayOneSoldAt);
        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(shiftId, user.Id, Money.Zero, DayOneClosedAt));

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(1, "the close wrote a rollup");

        // Cancel the bill on its own business date with no shift open - the owner's end-of-day
        // correction: close the shift, then notice a wrong bill. The rollup row is now stale.
        await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(sale.SaleId, "Rang the wrong item", DayOneCancelledAt));

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-06';"))
            .Should().Be(1, "the cancellation does not rebuild the rollup - that is the whole premise");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE status = 'CANCELLED';"))
            .Should().Be(1);

        var range = ReportDateRange.Custom(DayOne, DayOne);

        var query = fixture.Resolve<ISalesPeriodSummaryQuery>();
        var routed = await query.GetSalesSummaryAsync(range);
        var raw = await query.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routed.Should().Be(
            raw,
            "a date whose rollup a cancellation overtook must be read from raw, or routed and raw disagree");
        routed.BillCount.Should().Be(0, "the cancelled bill is not a completed bill");
        routed.GrossSales.Should().Be(Money.Zero, "the stale rollup's 200.00 must not be reported");
        routed.NetSales.Should().Be(Money.Zero);
        routed.TenderTotal.Should().Be(Money.Zero, "the stale rollup's 220.00 tender must not be reported");
    }

    /// <summary>
    /// The drift guard for the duplicated formulas: a closed, rolled-up date carrying a non-zero line
    /// discount and a non-zero bill discount, with a second rolled-up date whose completed bill is
    /// cancelled after its close. The routed report reads the discounted day from its rollup (written
    /// by <c>DailyRollupCalculator</c>, P3-T03) and the cancelled day from raw; the raw-only report
    /// reads both from raw. The two must agree, which is what pins the gross (<c>subtotal +
    /// line_discount</c>) and discount (<c>line_discount + bill_discount</c>) terms on both sides of
    /// the assembly boundary, and the <c>status = 'COMPLETED'</c> filter - none of which any earlier
    /// routing fixture exercised.
    /// </summary>
    [Fact]
    public async Task ARolledUpDateWithDiscountsAndACancelledBillReconcilesToRaw()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);
        await DisableBackupOnShiftCloseAsync(fixture);

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var variantId = await SeedTaxedVariantAsync(fixture);

        // 2026-09-06: 2 @ 100.00 with a line discount of 20.00 and a bill discount of 20.00.
        // subtotal 180.00 (net of the line discount), tax 18.00, total 178.00, cogs 120.00.
        // Close -> a rollup carrying both discount terms.
        var discounted = await CompleteAsync(
            fixture,
            variantId,
            quantity: 2m,
            DayOneSoldAt,
            lineDiscount: DiscountInput.OfAmount(Money.FromDecimal(20.00m)),
            billDiscount: DiscountInput.OfAmount(Money.FromDecimal(20.00m)));
        discounted.Total.Should().Be(Money.FromDecimal(178.00m), "the hand-worked figures below depend on it");

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(fixture.Resolve<ISession>().ShiftId!.Value, user.Id, Money.Zero, DayOneClosedAt));

        // 2026-09-10: two bills - a keeper (2 @ 100.00 -> total 220.00) and a mis-ring (3 @ 100.00
        // -> total 330.00). Close -> a rollup counting both, then cancel the mis-ring with no shift
        // open, which leaves the rollup stale and so distrusts the date.
        await fixture.Resolve<IOpenShift>().OpenAsync(new OpenShiftCommand(user.Id, Money.Zero, DayTwoSoldAt));
        var misRing = await CompleteAsync(fixture, variantId, quantity: 3m, DayTwoSoldAt);
        await CompleteAsync(
            fixture, variantId, quantity: 2m, DayTwoSoldAt.AddMinutes(30));

        await fixture.Resolve<ICloseShift>().CloseAsync(
            new CloseShiftCommand(
                fixture.Resolve<ISession>().ShiftId!.Value,
                user.Id,
                Money.Zero,
                DayTwoClosedAt,
                Note: "P3-T04 routing fixture - the drawer is not what this test is about"));

        (await fixture.CountAsync(
            "SELECT COUNT(*) FROM daily_sales_summary WHERE business_date = '2026-09-10';"))
            .Should().Be(1, "the close rolled up both of the day's bills");

        await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(misRing.SaleId, "Rang the wrong quantity", DayTwoCancelledAt));

        var range = ReportDateRange.Custom(DayOne, DayTwo);

        var query = fixture.Resolve<ISalesPeriodSummaryQuery>();
        var routed = await query.GetSalesSummaryAsync(range);
        var raw = await query.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routed.Should().Be(
            raw,
            "discounts and a cancelled bill must reconcile across the rollup/raw boundary");

        // 2026-09-06 from its rollup: gross 200.00, discounts 40.00, tax 18.00, net 160.00, tender 178.00.
        // 2026-09-10 from raw: the keeper only - gross 200.00, tax 20.00, net 200.00, tender 220.00.
        routed.BillCount.Should().Be(2, "the cancelled 2026-09-10 bill is not counted");
        routed.GrossSales.Should().Be(Money.FromDecimal(400.00m), "200.00 + 200.00, gross before any discount");
        routed.Discounts.Should().Be(Money.FromDecimal(40.00m), "20.00 line + 20.00 bill on 2026-09-06");
        routed.Tax.Should().Be(Money.FromDecimal(38.00m));
        routed.NetSales.Should().Be(Money.FromDecimal(360.00m), "160.00 + 200.00");
        routed.ReturnsValue.Should().Be(Money.Zero);
        routed.TenderTotal.Should().Be(Money.FromDecimal(398.00m), "178.00 + 220.00");

        var profit = fixture.Resolve<IProfitPeriodSummaryQuery>();
        var routedProfit = await profit.GetProfitSummaryAsync(range);
        var rawProfit = await profit.GetProfitSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routedProfit.Should().Be(rawProfit, "the cost side must reconcile across the boundary too");
        routedProfit.Cogs.Should().Be(Money.FromDecimal(240.00m), "120.00 discounted day + 120.00 keeper");
        routedProfit.GrossProfit.Should().Be(Money.FromDecimal(120.00m), "360.00 net - 240.00 cogs");
        routedProfit.MarginRate.Should().Be(120m / 360m);
    }

    // ---- Owner-only projection (AC-17, CLAUDE.md invariant 8) ----------------------------------

    [Fact]
    public async Task CashierSessionDtosCarryNoCostOrMarginFieldAndCannotReachProfit()
    {
        // The cashier-safe DTO has no cost-bearing property at all - the projection drops it
        // (CLAUDE.md invariant 8). The owner DTO keeps the cost figures, as the control.
        var cashierFields = typeof(SalesPeriodSummary).GetProperties().Select(property => property.Name).ToArray();

        cashierFields.Should().NotContain(name =>
            name.Contains("cost", StringComparison.OrdinalIgnoreCase)
            || name.Contains("cogs", StringComparison.OrdinalIgnoreCase)
            || name.Contains("profit", StringComparison.OrdinalIgnoreCase)
            || name.Contains("margin", StringComparison.OrdinalIgnoreCase));

        typeof(ProfitPeriodSummary).GetProperties().Select(property => property.Name)
            .Should().Contain(["Cogs", "GrossProfit", "MarginRate"], "the owner DTO is where cost lives");

        await using var fixture = await SaleFixture.CreateSignedInAsync(includeBackup: true);

        await fixture.Resolve<IUserAdministration>()
            .CreateAsync(new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        // A cashier can run the cost-free figures RPT-01/RPT-02 need...
        var summary = await fixture.Resolve<ISalesPeriodSummaryQuery>()
            .GetSalesSummaryAsync(ReportDateRange.For(ReportDatePreset.Today, DayOne));
        summary.NetSales.Should().Be(Money.Zero, "no trading on this date, and no cost field to leak either");

        // ...but cannot reach the cost-bearing query at all, and cannot resolve its concrete class.
        var profitQuery = fixture.Resolve<IProfitPeriodSummaryQuery>();
        Func<Task> act = () => profitQuery.GetProfitSummaryAsync(ReportDateRange.For(ReportDatePreset.Today, DayOne));

        await act.Should().ThrowAsync<NotAuthorisedException>();
        fixture.TryResolve<ProfitPeriodSummaryQuery>().Should().BeNull(
            "only the role-decorated interface is registered, never the concrete query");
        fixture.TryResolve<SalesPeriodSummaryQuery>().Should().BeNull(
            "the concrete query is internal and registered only behind its interface");

        // The shared engine is the other COGS-bearing object: ReadWithCogsAsync returns cost with no
        // role check of its own, so it must not be resolvable either. Each query builds its own from
        // IReportConnectionFactory instead (CLAUDE.md invariant 8, AC-17).
        fixture.TryResolve<PeriodFiguresReader>().Should().BeNull(
            "the engine carries COGS with no role check, so no container path may resolve it");
    }

    // ---- Shared seeding ------------------------------------------------------------------------

    private static Task<bool> SeedReturnNumberSequenceAsync(SaleFixture fixture) =>
        fixture.Resolve<INumberSequenceConfiguration>()
            .ConfigureAsync("RETURN", "RTN-", "{prefix}{yyyy}-{n:000000}", 1);

    /// <summary>
    /// Turns <c>backup.on_shift_close</c> off, exactly as
    /// <c>CloseShiftHandlerTests.FR_11_1_ABackupIsSkippedRatherThanAttemptedWhenDisabledOnShiftClose</c>
    /// does - so a shift close in these tests writes its rollup and nothing else (CLAUDE.md
    /// invariant 7 keeps a backup failure from blocking the close, but there is no reason to reach
    /// for the filesystem here at all). The trigger must still be registered, which is why the
    /// fixture is built with <c>includeBackup: true</c>.
    /// </summary>
    private static Task<SettingsSnapshot> DisableBackupOnShiftCloseAsync(SaleFixture fixture) =>
        fixture.Resolve<ISettings>().UpdateAsync(snapshot => snapshot with
        {
            Backup = snapshot.Backup with { BackupOnShiftClose = false },
        });

    private static async Task<CompletedSale> CompleteAsync(
        SaleFixture fixture,
        long variantId,
        decimal quantity,
        DateTimeOffset soldAt,
        DiscountInput? lineDiscount = null,
        DiscountInput? billDiscount = null)
    {
        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var lines = new List<SaleLineRequest> { new(variantId, quantity, Discount: lineDiscount) };

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines, billDiscount);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            user.Id,
            shiftId,
            soldAt,
            lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)],
            BillDiscount: billDiscount));
    }

    private static async Task<CreatedReturn> ReturnOneAsync(
        SaleFixture fixture, long saleId, long shiftId, long userId)
    {
        var saleLineId = await fixture.CountAsync(
            "SELECT id FROM sale_line WHERE sale_id = " + saleId.ToString(CultureInfo.InvariantCulture) + ";");

        return await fixture.Resolve<ICreateReturn>().CreateAsync(new CreateReturnCommand(
            saleId,
            userId,
            shiftId,
            DayOneReturnedAt,
            [new ReturnLineRequest(
                saleLineId, Quantity.FromDecimal(1m, saleLineId), ReturnDisposition.Sellable,
                "Changed mind")],
            RefundMethod.Cash));
    }

    /// <summary>
    /// A variant taxed at <see cref="TaxPercent"/>, priced at <see cref="UnitPrice"/> with an opening
    /// count posted through the ledger - the same technique <c>CloseShiftHandlerTests</c> and
    /// <c>XReportServiceTests</c> use, with round numbers so the hand-worked figures have no
    /// rounding step.
    /// </summary>
    private static Task<long> SeedTaxedVariantAsync(SaleFixture fixture)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<IStockLedger>();

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var uomId = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var taxClass = new TaxClass
            {
                Name = "Ten percent (P3-T04)",
                Rate = TaxRate.FromPercent(TaxPercent),
                Active = true,
            };

            context.Add(taxClass);
            await context.SaveChangesAsync(token);

            var product = new Product
            {
                Code = "RPT-001",
                Name = "Taxed widget",
                NameAlt = null,
                CategoryId = null,
                BrandId = null,
                BaseUomId = uomId,
                Type = "STANDARD",
                TaxClassId = taxClass.Id,
                CostAvg = Money.FromDecimal(UnitCost),
                ReorderLevel = 0,
                ReorderQty = 0,
                Location = "A1",
                NonReturnable = false,
                MinSellQty = 0,
                MaxDiscountRate = null,
                WarrantyDays = null,
                Notes = null,
                ImagePath = null,
                Active = true,
                CreatedAt = DayOneSoldAt,
                UpdatedAt = DayOneSoldAt,
            };

            context.Add(product);
            await context.SaveChangesAsync(token);

            context.Add(new ProductUom
            {
                ProductId = product.Id,
                UomId = uomId,
                ConversionFactor = UomConversion.Base.ToScaled(),
                SellingPrice = null,
                IsBase = true,
            });
            await context.SaveChangesAsync(token);

            var variant = new ProductVariant
            {
                ProductId = product.Id,
                Sku = "RPT-001-A",
                Attributes = """{"size":"std"}""",
                Price = Money.FromDecimal(UnitPrice),
                Active = true,
                CreatedAt = DayOneSoldAt,
            };

            context.Add(variant);
            await context.SaveChangesAsync(token);

            await ledger.PostAsync(
                new StockPosting(
                    variant.Id,
                    "OPENING",
                    Quantity.FromDecimal(100m, uomId),
                    Money.FromDecimal(UnitCost),
                    "OPENING",
                    RefDocId: null,
                    userId,
                    DayOneSoldAt),
                token);

            return variant.Id;
        });
    }
}
