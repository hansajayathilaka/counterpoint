using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Sales;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// One sale, end to end: scan, price, complete, and out through the print outbox
/// (SRS FR-3.28, FR-3.30, DM-03, SAD §7).
/// </summary>
public sealed class CompleteSaleTests
{
    /// <summary>The bill's business date drives the year in its number.</summary>
    private static readonly DateTimeOffset SoldAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    /// <summary>
    /// Property names a cashier's DTO must never carry (CLAUDE.md invariant 8).
    /// </summary>
    private static readonly string[] OwnerOnlyProperties = ["UnitCost", "Cost", "Margin"];

    [Fact]
    public async Task FR_3_2_ScanningTheSeededBarcodeAddsALineWithAPriceAndNoCost()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var item = await fixture.Resolve<IScanItem>().ScanAsync(FirstRunSeeder.SeededBarcode);

        item.Should().NotBeNull();
        item!.Description.Should().Be("Galvanised bolt M8");
        item.UnitPrice.Should().Be(Money.FromDecimal(12.50m));
        item.UomSymbol.Should().Be("pc");

        typeof(ScannedItem).GetProperties()
            .Select(property => property.Name)
            .Should().NotContain(
                OwnerOnlyProperties,
                "cost is excluded at the projection level, so a cashier's DTO has nothing to "
                + "leak (CLAUDE.md invariant 8, SRS NFR-S2, AC-17)");
    }

    [Fact]
    public async Task FR_3_29_TheFirstBillIsNumberedFromTheSequenceAndTheSecondFollowsIt()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var first = await CompleteOneAsync(fixture);
        var second = await CompleteOneAsync(fixture);

        first.BillNo.Should().Be("INV-2026-000001");
        second.BillNo.Should().Be("INV-2026-000002");

        var next = await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'SALE';");
        next.Should().Be("3", "the sequence row holds the next value to issue, never the last one issued");
    }

    [Fact]
    public async Task FR_3_28_CompletingABillWritesEveryTableOfTheTransactionAndNothingElse()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var completed = await CompleteOneAsync(fixture, quantity: 2m);

        completed.Total.Should().Be(Money.FromDecimal(25.00m));

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale_line WHERE sale_id = " + completed.SaleId + ";"))
            .Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM payment WHERE sale_id = " + completed.SaleId + ";"))
            .Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'SALE_COMPLETED';"))
            .Should().Be(1);
        (await fixture.CountAsync("SELECT COUNT(*) FROM print_job WHERE doc_id = " + completed.SaleId + ";"))
            .Should().Be(1);

        var status = await fixture.ScalarAsync("SELECT status FROM sale WHERE id = " + completed.SaleId + ";");
        status.Should().Be("COMPLETED");

        var businessDate = await fixture.ScalarAsync(
            "SELECT business_date FROM sale WHERE id = " + completed.SaleId + ";");
        businessDate.Should().Be("2026-09-06");
    }

    [Fact]
    public async Task DM_03_ASaleLineSnapshotsTheDescriptionThePriceAndTheCost()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var completed = await CompleteOneAsync(fixture, quantity: 2m);

        var line = await fixture.ScalarAsync(
            "SELECT description || '|' || unit_price || '|' || unit_cost || '|' || qty_base || '|' || line_total "
            + "FROM sale_line WHERE sale_id = " + completed.SaleId + ";");

        // 12.50 charged, 9.00 cost, 2 pieces, 25.00 line total - all scaled x10 000.
        line.Should().Be("Galvanised bolt M8|125000|90000|20000|250000");
    }

    [Fact]
    public async Task FR_3_12_TheLedgerRecordsTheMovementAndTheProjectionFollowsIt()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var openingQty = await fixture.ScalarAsync("SELECT qty_base FROM stock_balance;");
        openingQty.Should().Be("1000000", "the seeder opens with 100 pieces");

        var completed = await CompleteOneAsync(fixture, quantity: 2m);

        var movement = await fixture.ScalarAsync(
            "SELECT movement_type || '|' || qty_base || '|' || balance_after || '|' || ref_doc_type "
            + "|| '|' || ref_doc_id FROM stock_movement;");

        movement.Should().Be(
            "SALE|-20000|980000|SALE|" + completed.SaleId.ToString(CultureInfo.InvariantCulture),
            "balance_after is computed from the projection read inside the transaction, never "
            + "by summing the ledger (CLAUDE.md invariant 3)");

        var closingQty = await fixture.ScalarAsync("SELECT qty_base FROM stock_balance;");
        closingQty.Should().Be("980000", "the projection is written in the same transaction as the movement");
    }

    [Fact]
    public async Task NFR_S8_TheBillAndItsAuditRowAreChainedFromGenesis()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        await CompleteOneAsync(fixture);
        await CompleteOneAsync(fixture);

        var sales = await ReadAsync(fixture, context => context.Set<Sale>().OrderBy(row => row.Id).ToListAsync());

        sales.Should().HaveCount(2);
        sales[0].PrevHash.Should().Be(HashChain.GenesisHash);
        sales[1].PrevHash.Should().Be(sales[0].RowHash, "each bill links to the one before it");

        foreach (var sale in sales)
        {
            SaleHashChain.Verify(sale).Should().BeTrue(
                "row_hash must be SHA256(prev_hash || canonical_json(row)) over the row as stored "
                + "(CLAUDE.md invariant 6)");
        }

        var entries = await ReadAsync(
            fixture,
            context => context.Set<AuditLog>().OrderBy(row => row.Id).ToListAsync());

        entries.Should().HaveCount(2);
        entries[0].PrevHash.Should().Be(HashChain.GenesisHash);
        entries[1].PrevHash.Should().Be(entries[0].RowHash);
        entries.Should().OnlyContain(entry => AuditLogHashChain.Verify(entry));
    }

    [Fact]
    public async Task NFR_S8_EditingAStoredBillBreaksItsHash()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var completed = await CompleteOneAsync(fixture);
        var sale = await ReadAsync(
            fixture,
            context => context.Set<Sale>().SingleAsync(row => row.Id == completed.SaleId));

        // Not an UPDATE - the trigger would refuse that outright. This is the weaker attack the
        // chain exists for: a row rewritten by something that got past the triggers.
        sale.Total = Money.FromDecimal(1.00m);

        SaleHashChain.Verify(sale).Should().BeFalse();
    }

    [Fact]
    public async Task FR_7_8_TheReceiptIsQueuedInsideTheTransactionAndPrintedAfterIt()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var completed = await CompleteOneAsync(fixture);

        var queued = await fixture.ScalarAsync(
            "SELECT status || '|' || attempts || '|' || length(payload) FROM print_job WHERE id = "
            + completed.PrintJobId + ";");

        queued.Should().StartWith("PENDING|0|", "the sale transaction writes a row, never a printer call");
        queued!.Split('|')[2].Should().NotBe("0", "the outbox row carries the rendered ESC/POS stream");

        var printed = await fixture.Resolve<PrintWorker>().DrainAsync();

        printed.Should().Be(1);
        (await fixture.ScalarAsync("SELECT status FROM print_job WHERE id = " + completed.PrintJobId + ";"))
            .Should().Be("PRINTED");

        Directory.GetFiles(fixture.ReceiptDirectory, "*.bin")
            .Should().ContainSingle("FileReceiptPrinter writes the stream it was handed");
    }

    [Fact]
    public async Task AC_16_ABrokenPrinterDoesNotStopASaleOrRetryForEver()
    {
        await using var fixture = await SaleFixture.CreateAsync(PrinterFailureMode.FailEveryJob);

        // The sale itself must not notice.
        var completed = await CompleteOneAsync(fixture);
        completed.BillNo.Should().Be("INV-2026-000001");
        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(1);

        var worker = fixture.Resolve<PrintWorker>();

        await worker.DrainAsync();
        (await fixture.ScalarAsync("SELECT status || '|' || attempts FROM print_job;"))
            .Should().Be("PENDING|1", "one failed attempt leaves the job in the queue");

        await worker.DrainAsync();
        (await fixture.ScalarAsync("SELECT status || '|' || attempts FROM print_job;"))
            .Should().Be("PENDING|2");

        await worker.DrainAsync();
        (await fixture.ScalarAsync("SELECT status || '|' || attempts FROM print_job;"))
            .Should().Be("FAILED|3", "SAD §8: three attempts, then it stops and waits for a reprint");

        (await fixture.ScalarAsync("SELECT last_error IS NOT NULL FROM print_job;"))
            .Should().Be("1", "the reason is kept for the reprint queue and the status bar");

        // And the bill is still exactly as it was written.
        (await fixture.ScalarAsync("SELECT status FROM sale;")).Should().Be("COMPLETED");
    }

    [Fact]
    public async Task FR_3_30_TendersThatDoNotMatchTheTotalWriteNothingAtAll()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        var variantId = await SeededVariantIdAsync(fixture);

        var command = new CompleteSaleCommand(
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            SoldAt,
            [new SaleLineRequest(variantId, 1m)],
            [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(1.00m))]);

        var act = async () => await fixture.Resolve<ICompleteSale>().CompleteAsync(command);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must match exactly*");

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale;")).Should().Be(0);
        (await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement;")).Should().Be(0);
        (await fixture.ScalarAsync("SELECT next_val FROM number_sequence WHERE doc_type = 'SALE';"))
            .Should().Be("1", "the bill was refused before the number was allocated");
    }

    [Fact]
    public async Task NFR_P3_ABillSavesWellInsideTwoSeconds()
    {
        await using var fixture = await SaleFixture.CreateAsync();

        // Warm the connection, the model and the JIT: NFR-P3 is about saving a bill, not about
        // the first thing the process ever does. The absolute figure is HW-T07's, on the shop
        // terminal; this only catches something pathological.
        await CompleteOneAsync(fixture);

        var stopwatch = Stopwatch.StartNew();
        await CompleteOneAsync(fixture);
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(2),
            "bill completion has a 2 s budget (SRS NFR-P3); the on-terminal measurement is HW-T07");
    }

    private static async Task<CompletedSale> CompleteOneAsync(SaleFixture fixture, decimal quantity = 1m)
    {
        var lines = new List<SaleLineRequest>
        {
            new(await SeededVariantIdAsync(fixture), quantity),
        };

        // The tender comes from the Application layer's own quote, exactly as the screen does
        // it - the UI never multiplies a price by a quantity.
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(
            new CompleteSaleCommand(
                await SeededUserIdAsync(fixture),
                await SeededShiftIdAsync(fixture),
                SoldAt,
                lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");

    /// <summary>
    /// Reads rows back through the model, inside a unit of work, so the value converters that
    /// wrote them are the ones that read them.
    /// </summary>
    private static Task<T> ReadAsync<T>(SaleFixture fixture, Func<PosDbContext, Task<T>> read)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        return unitOfWork.ExecuteInTransactionAsync(async _ =>
        {
            using var context = unitOfWork.CreateDbContext();
            return await read(context);
        });
    }
}
