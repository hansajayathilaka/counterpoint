using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Import;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Import;

/// <summary>
/// Spreadsheet catalogue import and export - dry run, commit, idempotent re-import and the export
/// round trip (docs/03_PHASE_1_core_trading.md P1-T13, SRS FR-2.22, FR-2.23, AC-07).
/// </summary>
public sealed class CatalogueImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "counterpoint-import-tests", Guid.NewGuid().ToString("N"));

    public CatalogueImportTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task AC_07_OneHundredSkusImportFromASpreadsheetWithValidationReportAndCorrectStockAndPrices()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var rows = Enumerable.Range(1, 100)
            .Select(i => Row(
                code: $"AC07-{i:000}",
                name: $"Acceptance Item {i}",
                category: "Fasteners",
                unit: "Piece",
                taxClass: "Exempt",
                price: "12.50",
                cost: "6.00",
                qty: "40"))
            .ToArray();

        var file = WriteCsv("ac07.csv", rows);

        var beforeCount = await fixture.CountAsync("SELECT COUNT(*) FROM product;");

        var preview = await import.PreviewAsync(file, ImportColumnMapping.Default);
        preview.Counts.Errors.Should().Be(0, "every one of the 100 rows is valid");
        preview.Counts.Creates.Should().Be(100);

        var result = await import.CommitAsync(file, ImportColumnMapping.Default);
        result.Counts.Creates.Should().Be(100);
        result.Counts.Errors.Should().Be(0);

        var afterCount = await fixture.CountAsync("SELECT COUNT(*) FROM product;");
        (afterCount - beforeCount).Should().Be(100);

        // Correct resulting stock and prices for a sample of the rows (SRS AC-07).
        (await fixture.ScalarAsync(
            "SELECT pv.price FROM product p JOIN product_variant pv ON pv.product_id = p.id WHERE p.code = 'AC07-001';"))
            .Should().Be("125000", "12.50 scaled x10000");

        (await fixture.ScalarAsync(
            "SELECT sb.qty_base FROM product p JOIN product_variant pv ON pv.product_id = p.id " +
            "JOIN stock_balance sb ON sb.product_variant_id = pv.id WHERE p.code = 'AC07-100';"))
            .Should().Be("400000", "40 scaled x10000");
    }

    [Fact]
    public async Task FR_2_22_AFileWithTenDeliberateErrorsReportsAllTenAndWritesNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var rows = new List<string?[]>
        {
            Row(code: "", name: "Missing Code", category: null, unit: "Piece", taxClass: "Exempt", price: "10", cost: "0", qty: "0"),
            Row(code: "ERR-002", name: "", category: null, unit: "Piece", taxClass: "Exempt", price: "10", cost: "0", qty: "0"),
            Row(code: "ERR-003", name: "Unknown Unit", category: null, unit: "Furlong", taxClass: "Exempt", price: "10", cost: "0", qty: "0"),
            Row(code: "ERR-004", name: "Unknown Tax Class", category: null, unit: "Piece", taxClass: "Nonexistent", price: "10", cost: "0", qty: "0"),
            Row(code: "ERR-005", name: "Bad Price", category: null, unit: "Piece", taxClass: "Exempt", price: "not-a-number", cost: "0", qty: "0"),
            Row(code: "ERR-006", name: "Price Below Cost", category: null, unit: "Piece", taxClass: "Exempt", price: "1", cost: "5", qty: "0"),
            Row(code: "ERR-007", name: "Bad Qty", category: null, unit: "Piece", taxClass: "Exempt", price: "10", cost: "0", qty: "-3"),
            Row(code: "ERR-008", name: "Bad Warranty", category: null, unit: "Piece", taxClass: "Exempt", price: "10", cost: "0", qty: "0", warrantyDays: "not-a-number"),
            Row(code: "DUP-CODE", name: "Duplicate A", category: null, unit: "Piece", taxClass: "Exempt", price: "10", cost: "0", qty: "0"),
            Row(code: "DUP-CODE", name: "Duplicate B", category: null, unit: "Piece", taxClass: "Exempt", price: "10", cost: "0", qty: "0"),
        };

        var file = WriteCsv("errors.csv", [.. rows]);

        var beforeCount = await fixture.CountAsync("SELECT COUNT(*) FROM product;");

        var preview = await import.PreviewAsync(file, ImportColumnMapping.Default);
        preview.Counts.Errors.Should().Be(10, "all ten deliberately bad rows are reported");
        preview.ErrorSample.Should().HaveCount(10);
        preview.ErrorSample.Should().OnlyContain(row => row.Errors.Count > 0);

        var commit = () => import.CommitAsync(file, ImportColumnMapping.Default);
        await commit.Should().ThrowAsync<InvalidOperationException>().WithMessage("*10*");

        var afterCount = await fixture.CountAsync("SELECT COUNT(*) FROM product;");
        afterCount.Should().Be(beforeCount, "a file with any error writes nothing at all");
    }

    [Fact]
    public async Task FR_2_22_DryRunAndCommitProduceIdenticalCounts()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var rows = Enumerable.Range(1, 5)
            .Select(i => Row(code: $"DRY-{i}", name: $"Dry Run Item {i}", category: null, unit: "Piece", taxClass: "Exempt", price: "5", cost: "1", qty: "3"))
            .ToArray();

        var file = WriteCsv("dryrun.csv", rows);

        var preview = await import.PreviewAsync(file, ImportColumnMapping.Default);
        var commit = await import.CommitAsync(file, ImportColumnMapping.Default);

        commit.Counts.Should().BeEquivalentTo(preview.Counts);
    }

    [Fact]
    public async Task FR_2_22_ReimportingTheSameFileUpdatesRatherThanDuplicating()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var rows = Enumerable.Range(1, 20)
            .Select(i => Row(code: $"RE-{i:00}", name: $"Reimport Item {i}", category: null, unit: "Piece", taxClass: "Exempt", price: "9", cost: "2", qty: "15"))
            .ToArray();

        var file = WriteCsv("reimport.csv", rows);

        var first = await import.CommitAsync(file, ImportColumnMapping.Default);
        first.Counts.Creates.Should().Be(20);
        first.Counts.Updates.Should().Be(0);

        var afterFirst = await fixture.CountAsync("SELECT COUNT(*) FROM product WHERE code LIKE 'RE-%';");

        var second = await import.CommitAsync(file, ImportColumnMapping.Default);
        second.Counts.Creates.Should().Be(0, "every code already exists");
        second.Counts.Updates.Should().Be(20);

        var afterSecond = await fixture.CountAsync("SELECT COUNT(*) FROM product WHERE code LIKE 'RE-%';");
        afterSecond.Should().Be(afterFirst, "re-importing must update, never duplicate");
    }

    [Fact]
    public async Task FR_2_23_ExportRoundTripsWithNoChangesDetected()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var rows = Enumerable.Range(1, 10)
            .Select(i => Row(code: $"XP-{i:00}", name: $"Export Item {i}", category: "Fasteners", unit: "Piece", taxClass: "Exempt", price: "7.25", cost: "3.10", qty: "18"))
            .ToArray();

        var importFile = WriteCsv("export-source.csv", rows);
        await import.CommitAsync(importFile, ImportColumnMapping.Default);

        var qtyBefore = await fixture.ScalarAsync(
            "SELECT sb.qty_base FROM product p JOIN product_variant pv ON pv.product_id = p.id " +
            "JOIN stock_balance sb ON sb.product_variant_id = pv.id WHERE p.code = 'XP-01';");
        var movementCountBefore = await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement;");

        var exportFile = Path.Combine(_root, "export.csv");
        await import.ExportCatalogueAsync(exportFile);
        File.Exists(exportFile).Should().BeTrue();

        // Re-importing the shop's own export needs no mapping profile at all - it is already in
        // ImportColumnMapping.Default's shape (SRS FR-2.23).
        var preview = await import.PreviewAsync(exportFile, ImportColumnMapping.Default);
        preview.Counts.Errors.Should().Be(0);

        var reimport = await import.CommitAsync(exportFile, ImportColumnMapping.Default);
        reimport.Counts.Errors.Should().Be(0);

        var qtyAfter = await fixture.ScalarAsync(
            "SELECT sb.qty_base FROM product p JOIN product_variant pv ON pv.product_id = p.id " +
            "JOIN stock_balance sb ON sb.product_variant_id = pv.id WHERE p.code = 'XP-01';");
        var movementCountAfter = await fixture.CountAsync("SELECT COUNT(*) FROM stock_movement;");

        qtyAfter.Should().Be(qtyBefore, "the exported quantity matches what is already on hand, so re-importing it posts no movement");
        movementCountAfter.Should().Be(movementCountBefore, "no changes were detected, so nothing new was posted to the ledger");
    }

    [Fact]
    public async Task FR_2_23_ExportWritesAnXlsxWorkbookThatReimportsCleanly()
    {
        // The export/import round trip proven above uses CSV; this proves the ClosedXML side of
        // the same ISpreadsheetReader/ISpreadsheetWriter split reads and writes an equally clean
        // workbook (SRS FR-2.22, FR-2.23).
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        var rows = Enumerable.Range(1, 3)
            .Select(i => Row(code: $"XL-{i}", name: $"Xlsx Item {i}", category: null, unit: "Piece", taxClass: "Exempt", price: "3.50", cost: "1.00", qty: "9"))
            .ToArray();

        var csvFile = WriteCsv("xlsx-source.csv", rows);
        var committed = await import.CommitAsync(csvFile, ImportColumnMapping.Default);
        committed.Counts.Creates.Should().Be(3);

        var xlsxFile = Path.Combine(_root, "export.xlsx");
        await import.ExportCatalogueAsync(xlsxFile);
        File.Exists(xlsxFile).Should().BeTrue();

        var preview = await import.PreviewAsync(xlsxFile, ImportColumnMapping.Default);
        preview.Counts.Errors.Should().Be(0);
        preview.Counts.Updates.Should().BeGreaterThanOrEqualTo(3);
        preview.Counts.Creates.Should().Be(0, "every product the workbook names already exists");
    }

    private string WriteCsv(string fileName, IReadOnlyList<string?[]> rows)
    {
        var path = Path.Combine(_root, fileName);

        using var writer = new StreamWriter(path);
        writer.WriteLine(string.Join(',', CatalogueExportColumns.Headers));

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',', row.Select(Escape)));
        }

        return path;
    }

    private static string Escape(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Contains(',', StringComparison.Ordinal) ? $"\"{value}\"" : value;

    /// <summary>One row, in <see cref="CatalogueExportColumns.Headers"/> order.</summary>
    private static string?[] Row(
        string code,
        string name,
        string? category,
        string unit,
        string taxClass,
        string price,
        string cost,
        string qty,
        string? nameAlt = null,
        string? brand = null,
        string? type = null,
        string? location = null,
        string? nonReturnable = null,
        string? warrantyDays = null,
        string? notes = null,
        string? barcode = null) =>
    [
        code, name, nameAlt, category, brand, unit, type, taxClass, location,
        nonReturnable, warrantyDays, notes, barcode, price, cost, qty,
    ];

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
