using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// RPT-15, damage and shrinkage (task P3-T06 "Do this" #1), and the additive calendar-day bounds on
/// <see cref="AdjustmentHistoryFilter"/> it reads through, against the ledger in <see cref="StockCashDataset"/>:
/// </summary>
/// <remarks>
/// <code>
/// A1 Sep 8 09:00      Bolt  ADJUSTMENT -4  "Stock count correction"  at 59.50   = -238.00
/// A2 Sep 8 23:59:59   Bolt  DAMAGE     -6  "Water damage"            at 59.50   = -357.00
/// A3 Sep 8 10:00      Drill ADJUSTMENT +3  "Found in back store"     at 149.50  = +448.50
/// A4 Sep 9 00:00:00   Drill DAMAGE     -2  "Water damage"            at 149.50  = -299.00
/// A5 Sep 9 11:30      Drill ADJUSTMENT -1  "Stock count correction"  at 149.50  = -149.50
/// R2 Sep 7            Drill DAMAGED return of 1, reason "Cracked housing", cost snapshot 150.00 = -150.00 (no stock movement)
/// </code>
/// The value is the cost recorded on the movement (or the return line), not the catalogue's current average: Bolt's
/// average is 59.75 by the end of the history (G4) and Drill's 149.50 against R2's 150.00 snapshot.
/// </remarks>
[Collection(StockCashReportFixture.Name)]
public sealed class DamageAdjustmentReportTests(StockCashReportFixture fixture)
{
    private static Money M(decimal amount) => Money.FromDecimal(amount);

    private IDamageAdjustmentReportQuery Query => fixture.Host.Resolve<IDamageAdjustmentReportQuery>();

    [Fact]
    public async Task RPT_15_DamageWriteOffsAdjustmentsAndDamagedReturnsAreGroupedByReasonAtTheirRecordedCost()
    {
        var report = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 6), new(2026, 9, 9)));

        report.Rows.Select(row => (row.Source, row.Reason, row.Count, row.QtyBase.Value, row.Value)).Should().Equal(
        [
            (DamageSource.Damage, "Water damage", 2, -8m, M(-656.00m)),
            (DamageSource.Adjustment, "Stock count correction", 2, -5m, M(-387.50m)),
            (DamageSource.DamagedReturn, "Cracked housing", 1, -1m, M(-150.00m)),
            (DamageSource.Adjustment, "Found in back store", 1, 3m, M(448.50m)),
        ]);

        report.NetValue.Should().Be(M(-745.00m), "-656.00 - 387.50 - 150.00 + 448.50");
        report.TotalLoss.Should().Be(M(1193.50m), "the individual losses: 357.00 + 299.00 + 238.00 + 149.50 + 150.00");
        report.TotalGain.Should().Be(M(448.50m));
        (report.TotalGain - report.TotalLoss).Should().Be(report.NetValue);
        report.Range.Should().Be(ReportDateRange.Custom(new(2026, 9, 6), new(2026, 9, 9)));
    }

    [Fact]
    public async Task RPT_15_ADamagedReturnIsAlsoAStockLossButPostsNoLedgerMovementSoNothingIsCountedTwice()
    {
        // R2 returned a Drill as DAMAGED: not restocked, so the ledger has no RETURN_IN for it - only R1 and R3's Bolts.
        (await fixture.Host.CountAsync("SELECT COUNT(*) FROM stock_movement WHERE movement_type = 'RETURN_IN';"))
            .Should().Be(2);

        var sep7 = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 7), new(2026, 9, 7)));

        sep7.Rows.Select(row => (row.Source, row.Reason, row.Count, row.QtyBase.Value, row.Value)).Should().Equal(
            [(DamageSource.DamagedReturn, "Cracked housing", 1, -1m, M(-150.00m))],
            "the return line's own cost snapshot (150.00), not the catalogue's current 149.50");
        sep7.NetValue.Should().Be(M(-150.00m));
        sep7.TotalLoss.Should().Be(M(150.00m));
        sep7.TotalGain.Should().Be(Money.Zero);
    }

    [Fact]
    public async Task RPT_15_ADayRangeTakesAMovementAtTwentyThreeFiftyNineAndLeavesOneAtMidnight()
    {
        var sep8 = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 8), new(2026, 9, 8)));

        sep8.Rows.Select(row => (row.Source, row.Reason, row.Count, row.QtyBase.Value, row.Value)).Should().Equal(
        [
            (DamageSource.Damage, "Water damage", 1, -6m, M(-357.00m)),
            (DamageSource.Adjustment, "Stock count correction", 1, -4m, M(-238.00m)),
            (DamageSource.Adjustment, "Found in back store", 1, 3m, M(448.50m)),
        ]);
        sep8.NetValue.Should().Be(M(-146.50m));
        sep8.TotalLoss.Should().Be(M(595.00m));
        sep8.TotalGain.Should().Be(M(448.50m));

        var sep9 = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 9), new(2026, 9, 9)));

        sep9.Rows.Select(row => (row.Source, row.Reason, row.Count, row.QtyBase.Value, row.Value)).Should().Equal(
        [
            (DamageSource.Damage, "Water damage", 1, -2m, M(-299.00m)),
            (DamageSource.Adjustment, "Stock count correction", 1, -1m, M(-149.50m)),
        ]);
        sep9.NetValue.Should().Be(M(-448.50m));
        sep9.TotalGain.Should().Be(Money.Zero);
    }

    [Fact]
    public async Task RPT_15_OnlyAdjustmentAndDamageMovementsAreReportedNotSalesReceiptsReturnsOrStockTakes()
    {
        // Sep 1-30 holds a stock take (-7 Drill), four receipts, sales and returns; none of them is in this report.
        var report = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 1), new(2026, 9, 30)));

        report.Rows.Should().HaveCount(4);
        report.Rows.Sum(row => row.Count).Should().Be(6, "five ledger movements and one damaged return line");
        report.NetValue.Should().Be(M(-745.00m));

        var empty = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 10), new(2026, 9, 30)));

        empty.Rows.Should().BeEmpty("the stock take, the receipts and the cancelled Hinge bill are not adjustments");
        empty.NetValue.Should().Be(Money.Zero);
        empty.TotalLoss.Should().Be(Money.Zero);
        empty.TotalGain.Should().Be(Money.Zero);
    }

    [Fact]
    public async Task RPT_15_AdjustmentsAreValuedAtTheCostOnTheMovementNotTheCurrentAverage()
    {
        // Bolt's moving average is 59.75 now (G4 was dear), but A1 and A2 were posted at 59.50.
        (await fixture.Host.CountAsync("SELECT cost_avg FROM stock_balance WHERE product_variant_id = " + fixture.Data.Sales.BoltVariantId + ";"))
            .Should().Be(597_500, "the precondition: the average has moved on since the adjustments");

        var report = await Query.GetReportAsync(ReportDateRange.Custom(new(2026, 9, 8), new(2026, 9, 8)));

        report.Rows.Single(row => row.Reason == "Water damage").Value.Should().Be(M(-357.00m), "6 x 59.50, not 6 x 59.75 = 358.50");
        report.Rows.Single(row => row.Reason == "Stock count correction").Value.Should().Be(M(-238.00m), "4 x 59.50, not 239.00");
    }

    // ---- The additive date bounds on AdjustmentHistoryFilter ------------------------------------

    [Fact]
    public async Task AdjustmentHistory_FromDateAndToDateBoundByCalendarDayAndAreNewestFirst()
    {
        var history = fixture.Host.Resolve<IAdjustmentHistoryQuery>();
        var sep8 = new DateOnly(2026, 9, 8);
        var sep9 = new DateOnly(2026, 9, 9);

        var all = await history.ListAsync(new AdjustmentHistoryFilter());
        all.Should().HaveCount(5);

        var onlyFrom = await history.ListAsync(new AdjustmentHistoryFilter(FromDate: sep9));
        onlyFrom.Select(line => line.Reason).Should().Equal("Stock count correction", "Water damage");
        onlyFrom.Select(line => line.OccurredAt.Day).Should().OnlyContain(day => day == 9);

        var onlyTo = await history.ListAsync(new AdjustmentHistoryFilter(ToDate: sep8));
        onlyTo.Select(line => line.Reason).Should().Equal(
            ["Water damage", "Found in back store", "Stock count correction"],
            "ToDate is inclusive of the whole day: the 23:59:59 write-off is in, newest first");
        onlyTo.Select(line => line.OccurredAt.Day).Should().OnlyContain(day => day == 8);

        var oneDay = await history.ListAsync(new AdjustmentHistoryFilter(FromDate: sep9, ToDate: sep9));
        oneDay.Select(line => line.MovementType).Should().Equal("ADJUSTMENT", "DAMAGE");

        var both = await history.ListAsync(new AdjustmentHistoryFilter(FromDate: sep8, ToDate: sep9));
        both.Should().HaveCount(5);

        (await history.ListAsync(new AdjustmentHistoryFilter(FromDate: new DateOnly(2026, 9, 10)))).Should().BeEmpty();
        (await history.ListAsync(new AdjustmentHistoryFilter(ToDate: new DateOnly(2026, 9, 7)))).Should().BeEmpty();
    }

    [Fact]
    public async Task AdjustmentHistory_TheDateBoundsAreAdditiveToTheTypeAndTheExistingInstantBounds()
    {
        var history = fixture.Host.Resolve<IAdjustmentHistoryQuery>();
        var sep8 = new DateOnly(2026, 9, 8);
        var sep9 = new DateOnly(2026, 9, 9);

        var damageOnly = await history.ListAsync(new AdjustmentHistoryFilter(AdjustmentType.Damage, FromDate: sep8, ToDate: sep9));
        damageOnly.Select(line => (line.MovementType, line.Reason)).Should().Equal(
            [("DAMAGE", "Water damage"), ("DAMAGE", "Water damage")]);
        damageOnly.Select(line => line.QtyBase.Value).Should().Equal(-2m, -6m);

        // An instant bound still applies on top of a day bound: from 10:00 on Sep 8, and day bounds Sep 9 only.
        var instant = new DateTimeOffset(2026, 9, 8, 10, 0, 0, SalesReportDataset.ShopOffset);
        var both = await history.ListAsync(new AdjustmentHistoryFilter(From: instant, FromDate: sep9));
        both.Should().HaveCount(2, "the instant alone would admit A2 and A3 as well; the day bound narrows it to Sep 9");

        var instantOnly = await history.ListAsync(new AdjustmentHistoryFilter(From: instant));
        instantOnly.Should().HaveCount(4, "A1 at 09:00 is before the instant; A3 at exactly 10:00 is on it");
    }
}
