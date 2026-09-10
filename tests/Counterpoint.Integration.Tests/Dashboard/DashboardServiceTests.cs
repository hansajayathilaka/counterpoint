using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Shifts;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Dashboard;

/// <summary>
/// The compact home-screen dashboard (SRS FR-9.7): today's sales, bill count, average bill, cash
/// in drawer, low-stock count, last backup status.
/// </summary>
public sealed class DashboardServiceTests
{
    /// <summary>The fixture's clock (<c>SaleFixture.CreateAsync</c>) - 2026-09-06, +05:30.</summary>
    private static readonly DateTimeOffset SoldAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_9_7_DashboardFiguresMatchHandComputedValuesForASeededDay()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        // A real opening float, not the seeded shift's zero, so "cash in drawer" is provably more
        // than just today's cash sales.
        await fixture.ExecuteAsync("UPDATE shift SET status = 'CLOSED' WHERE status = 'OPEN';");
        var user = fixture.Resolve<ISession>().CurrentUser!;
        var opened = await fixture.Resolve<IOpenShift>().OpenAsync(
            new OpenShiftCommand(user.Id, Money.FromDecimal(5000m), SoldAt));

        // Two bills: one paid cash, one paid card - only the cash one belongs in the drawer.
        await CompleteOneAsync(fixture, opened.ShiftId, user.Id, quantity: 1m, TenderTypes.Cash); // 12.50
        await CompleteOneAsync(fixture, opened.ShiftId, user.Id, quantity: 2m, TenderTypes.Card); // 25.00

        // FR-4.17: a product at or below its configured reorder level. The seeded product has 100
        // pieces on hand (FirstRunSeeder) and reorder_level = 0 (not tracked) by default.
        await fixture.ExecuteAsync("UPDATE product SET reorder_level = 2000000 WHERE code = 'SKEL-001';");

        var dashboard = await fixture.Resolve<IDashboardQueries>().GetSummaryAsync();

        dashboard.TodaysSales.Should().Be(Money.FromDecimal(37.50m));
        dashboard.BillCount.Should().Be(2);
        dashboard.AverageBill.Should().Be(Money.FromDecimal(18.75m));
        dashboard.CashInDrawer.Should().Be(Money.FromDecimal(5012.50m), "5000.00 opening float + 12.50 cash sale; the card sale never touches the drawer");
        dashboard.LowStockCount.Should().Be(1);
        dashboard.LastBackup.Should().BeNull("nothing has recorded a backup yet - P1-T15 is what writes backup_record");
    }

    [Fact]
    public async Task FR_9_7_YesterdaysSalesAreExcludedFromTodaysDashboardFigures()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var user = fixture.Resolve<ISession>().CurrentUser!;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;
        var variantId = await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

        await CompleteOneAsync(fixture, shiftId, user.Id, quantity: 1m, TenderTypes.Cash); // today, 12.50

        // A bill from yesterday, inserted directly - what a rollup or a report would find in the
        // history a real shop accumulates. Append-only triggers guard UPDATE and DELETE on
        // `sale`, not INSERT, so a plain insert with placeholder hash-chain columns is exactly
        // how TradingDaySeed (this suite's own fixture for schema-conformance tests) seeds one.
        await fixture.ExecuteAsync(
            "INSERT INTO sale (bill_no, sold_at, business_date, customer_id, user_id, shift_id, "
            + "subtotal, line_discount, bill_discount, tax, rounding, total, cogs, status, "
            + "cancelled_by, cancelled_at, note, prev_hash, row_hash) VALUES ("
            + "'INV-2026-999999', '2026-09-05T10:00:00.000+05:30', '2026-09-05', NULL, "
            + user.Id + ", " + shiftId + ", "
            + "1000000, 0, 0, 0, 0, 1000000, 0, 'COMPLETED', NULL, NULL, NULL, "
            + "'GENESIS', 'placeholder-row-hash');");

        var dashboard = await fixture.Resolve<IDashboardQueries>().GetSummaryAsync();

        dashboard.BillCount.Should().Be(1, "yesterday's bill must not be counted as today's");
        dashboard.TodaysSales.Should().Be(Money.FromDecimal(12.50m));

        _ = variantId;
    }

    [Fact]
    public async Task FR_9_7_LastBackupStatusReflectsTheMostRecentBackupRecord()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var store = fixture.Resolve<IBackupRecordStore>();

        await store.RecordAsync(new NewBackupRecord(
            "counterpoint-20260904.cpb",
            new DateTimeOffset(2026, 9, 4, 19, 0, 0, TimeSpan.FromHours(5.5)),
            1024,
            "checksum-1",
            "schema-1",
            "/backups/counterpoint-20260904.cpb",
            "NA",
            "SKIPPED"));

        await store.RecordAsync(new NewBackupRecord(
            "counterpoint-20260906.cpb",
            new DateTimeOffset(2026, 9, 6, 19, 0, 0, TimeSpan.FromHours(5.5)),
            2048,
            "checksum-2",
            "schema-1",
            "/backups/counterpoint-20260906.cpb",
            "OK",
            "SKIPPED"));

        var dashboard = await fixture.Resolve<IDashboardQueries>().GetSummaryAsync();

        dashboard.LastBackup.Should().NotBeNull();
        dashboard.LastBackup!.TakenAt.Should().Be(new DateTimeOffset(2026, 9, 6, 19, 0, 0, TimeSpan.FromHours(5.5)));
        dashboard.LastBackup.UsbStatus.Should().Be("OK");
    }

    [Fact]
    public async Task FR_4_17_LowStockCountIgnoresAProductWithNoReorderLevelConfigured()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        (await fixture.Resolve<IDashboardQueries>().GetSummaryAsync())
            .LowStockCount.Should().Be(0, "the seeded product's reorder_level is the default zero - 'not tracked'");

        // A reorder level set, but stock still comfortably above it.
        await fixture.ExecuteAsync("UPDATE product SET reorder_level = 500000 WHERE code = 'SKEL-001';"); // 50 pieces; 100 on hand

        (await fixture.Resolve<IDashboardQueries>().GetSummaryAsync())
            .LowStockCount.Should().Be(0, "100 pieces on hand is above the 50-piece reorder level");

        // Now at or below it.
        await fixture.ExecuteAsync("UPDATE product SET reorder_level = 2000000 WHERE code = 'SKEL-001';"); // 200 pieces; 100 on hand

        (await fixture.Resolve<IDashboardQueries>().GetSummaryAsync())
            .LowStockCount.Should().Be(1);
    }

    private static async Task<CompletedSale> CompleteOneAsync(
        SaleFixture fixture,
        long shiftId,
        long userId,
        decimal quantity,
        string tenderType)
    {
        var variantId = await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");
        var lines = new List<SaleLineRequest> { new(variantId, quantity) };

        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                userId,
                shiftId,
                SoldAt,
                lines,
                [new TenderRequest(tenderType, quote.Total)]));
    }
}
