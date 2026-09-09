using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Application.Sales;
using Counterpoint.Devices.Printing;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// Reprinting a past bill: marked DUPLICATE on the paper, queued through the same outbox a first
/// print uses, and logged (SRS FR-3.36, FR-7.5, FR-7.6, PRT-08).
/// </summary>
public sealed class ReprintReceiptTests
{
    private static readonly DateTimeOffset SoldAt = new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_7_6_AReprintRendersDuplicateAndWritesAnAuditRow()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var completed = await CompleteOneAsync(fixture);

        // Drain the original print job first, so the reprint's own file is the only thing left
        // to inspect afterwards.
        await fixture.Resolve<PrintWorker>().DrainAsync();
        Directory.GetFiles(fixture.ReceiptDirectory, "*.bin").Should().ContainSingle();

        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'RECEIPT_REPRINTED';"))
            .Should().Be(0, "nothing has been reprinted yet");

        // FileReceiptPrinter names its file from the clock, to millisecond resolution; without
        // moving it forward the reprint would land on the same file name as the original and
        // silently overwrite it rather than proving two files exist.
        ((FixedTimeProvider)fixture.Resolve<TimeProvider>()).Advance(TimeSpan.FromSeconds(30));

        var reprinted = await fixture.Resolve<IReprintReceipt>().ReprintAsync(completed.SaleId);

        reprinted.BillNo.Should().Be(completed.BillNo, "a reprint keeps the original bill number");

        (await fixture.CountAsync("SELECT COUNT(*) FROM print_job WHERE doc_id = " + completed.SaleId + ";"))
            .Should().Be(2, "the original print job and the reprint");

        (await fixture.ScalarAsync(
                "SELECT is_duplicate FROM print_job WHERE id = " + reprinted.PrintJobId + ";"))
            .Should().Be("1", "print_job.is_duplicate (FR-7.6)");

        (await fixture.CountAsync("SELECT COUNT(*) FROM audit_log WHERE action = 'RECEIPT_REPRINTED';"))
            .Should().Be(1, "the reprint is a new audit_log row - append-only, never an update (CLAUDE.md invariant 5)");

        await fixture.Resolve<PrintWorker>().DrainAsync();

        var files = Directory.GetFiles(fixture.ReceiptDirectory, "*.bin");
        files.Should().HaveCount(2, "the original and the reprint each produced their own file");

        var reprintBytes = await File.ReadAllBytesAsync(MostRecentlyWritten(files));
        ContainsAscii(reprintBytes, "DUPLICATE").Should().BeTrue(
            "PRT-08: a reprint must carry a DUPLICATE line");
    }

    [Fact]
    public async Task FR_3_36_TheOriginalPrintIsNeverMarkedDuplicate()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var completed = await CompleteOneAsync(fixture);
        await fixture.Resolve<PrintWorker>().DrainAsync();

        var files = Directory.GetFiles(fixture.ReceiptDirectory, "*.bin");
        var originalBytes = await File.ReadAllBytesAsync(files[0]);

        ContainsAscii(originalBytes, "DUPLICATE").Should().BeFalse();

        completed.SaleId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReprintingABillThatDoesNotExistFails()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var act = async () => await fixture.Resolve<IReprintReceipt>().ReprintAsync(999_999);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static async Task<CompletedSale> CompleteOneAsync(SaleFixture fixture)
    {
        var lines = new List<SaleLineRequest> { new(await SeededVariantIdAsync(fixture), 1m) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            await SeededUserIdAsync(fixture),
            await SeededShiftIdAsync(fixture),
            SoldAt,
            lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)]));
    }

    /// <summary>Whether the raw ESC/POS byte stream contains <paramref name="text"/> as plain ASCII.</summary>
    private static bool ContainsAscii(byte[] bytes, string text) =>
        Encoding.ASCII.GetString(bytes).Contains(text, StringComparison.Ordinal);

    private static string MostRecentlyWritten(string[] files)
    {
        var latest = files[0];
        var latestTime = File.GetLastWriteTimeUtc(latest);

        foreach (var file in files)
        {
            var time = File.GetLastWriteTimeUtc(file);
            if (time > latestTime)
            {
                latest = file;
                latestTime = time;
            }
        }

        return latest;
    }

    private static async Task<long> SeededVariantIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");

    private static async Task<long> SeededUserIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");

    private static async Task<long> SeededShiftIdAsync(SaleFixture fixture) =>
        await fixture.CountAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;");
}
