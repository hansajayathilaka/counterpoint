using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Import;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.Ui.ViewModels.Catalogue;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Import;

/// <summary>
/// <see cref="ImportTabViewModel.HasCleanPreview"/> - the flag that gates <c>CommitCommand</c>
/// (docs/03_PHASE_1_core_trading.md P1-T13 "a bad import is very hard to unwind", SRS FR-2.22).
/// </summary>
/// <remarks>
/// Constructed directly against real, DI-resolved services over a real SQLite file, the same
/// pattern <c>ProductTabViewModelUomTests</c> uses for another catalogue tab - no mock of
/// <see cref="ICatalogueImportService"/> or <see cref="ISpreadsheetReader"/>, so a defect in the
/// gate would show up as a real row written to <c>product</c>, not as a broken mock expectation.
/// </remarks>
public sealed class ImportTabViewModelGatingTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "counterpoint-import-vm-tests", Guid.NewGuid().ToString("N"));

    public ImportTabViewModelGatingTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ADryRunOverACleanFileSetsHasCleanPreviewTrue()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var screen = NewScreen(fixture);

        screen.FilePath = WriteCleanCsv("clean.csv", "VM-CLEAN");
        await screen.LoadHeadersCommand.ExecuteAsync(null);
        await screen.PreviewCommand.ExecuteAsync(null);

        screen.HasCleanPreview.Should().BeTrue("the file has no errors and at least one row");
        screen.Status.Should().Be("Dry run clean. Ready to commit.");
    }

    [Fact]
    public async Task ChangingTheFilePathAfterACleanPreviewResetsTheFlag()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var screen = NewScreen(fixture);

        screen.FilePath = WriteCleanCsv("clean.csv", "VM-RESET-FILE");
        await screen.LoadHeadersCommand.ExecuteAsync(null);
        await screen.PreviewCommand.ExecuteAsync(null);
        screen.HasCleanPreview.Should().BeTrue("preconditon: the dry run must have come back clean first");

        screen.FilePath = WriteCleanCsv("clean-2.csv", "VM-RESET-FILE-2");

        screen.HasCleanPreview.Should().BeFalse(
            "the file changed since the clean preview, so the old preview no longer describes what commit would write");
    }

    [Fact]
    public async Task ChangingAMappingRowsSelectedHeaderAfterACleanPreviewResetsTheFlag()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var screen = NewScreen(fixture);

        screen.FilePath = WriteCleanCsv("clean.csv", "VM-RESET-MAPPING");
        await screen.LoadHeadersCommand.ExecuteAsync(null);
        await screen.PreviewCommand.ExecuteAsync(null);
        screen.HasCleanPreview.Should().BeTrue("preconditon: the dry run must have come back clean first");

        var codeRow = screen.MappingRows.First(r => r.FieldKey == nameof(ImportColumnMapping.Code));
        codeRow.SelectedHeader = ImportFieldMappingRowViewModel.NotMapped;

        screen.HasCleanPreview.Should().BeFalse(
            "the mapping changed since the clean preview, so the old preview no longer describes what commit would write");
    }

    [Fact]
    public async Task ADryRunWithErrorsLeavesTheFlagFalse()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var screen = NewScreen(fixture);

        // A blank code is a validation error (missing required field), same as
        // CatalogueImportTests.FR_2_22_AFileWithTenDeliberateErrorsReportsAllTenAndWritesNothing.
        screen.FilePath = WriteCsv("errors.csv",
        [
            Row(code: "", name: "Missing Code", unit: "Piece", taxClass: "Exempt", price: "10", cost: "0", qty: "0"),
        ]);
        await screen.LoadHeadersCommand.ExecuteAsync(null);
        await screen.PreviewCommand.ExecuteAsync(null);

        screen.HasCleanPreview.Should().BeFalse("the dry run found at least one error row");
        screen.Status.Should().Contain("error");
    }

    [Fact]
    public async Task CommittingWithoutAPriorCleanPreviewIsRefusedAndWritesNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var screen = NewScreen(fixture);

        screen.FilePath = WriteCleanCsv("clean.csv", "VM-NO-PREVIEW");
        await screen.LoadHeadersCommand.ExecuteAsync(null);
        // Deliberately no PreviewCommand call: HasCleanPreview is still false.
        screen.HasCleanPreview.Should().BeFalse();

        var beforeCount = await fixture.CountAsync("SELECT COUNT(*) FROM product WHERE code = 'VM-NO-PREVIEW';");

        await screen.CommitCommand.ExecuteAsync(null);

        screen.Status.Should().Be("Run a dry run with zero errors first.",
            "CommitAsync itself refuses when HasCleanPreview is false - this is not just an XAML IsEnabled binding");

        var afterCount = await fixture.CountAsync("SELECT COUNT(*) FROM product WHERE code = 'VM-NO-PREVIEW';");
        afterCount.Should().Be(beforeCount, "the ViewModel must not have called the import service at all");
    }

    [Fact]
    public async Task CommittingAfterACleanPreviewSucceedsAndResetsTheFlagAfterwards()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var screen = NewScreen(fixture);

        screen.FilePath = WriteCleanCsv("clean.csv", "VM-COMMIT-OK");
        await screen.LoadHeadersCommand.ExecuteAsync(null);
        await screen.PreviewCommand.ExecuteAsync(null);
        screen.HasCleanPreview.Should().BeTrue();

        await screen.CommitCommand.ExecuteAsync(null);

        screen.Status.Should().StartWith("Committed:");
        screen.HasCleanPreview.Should().BeFalse(
            "a commit is a one-shot event - a second commit needs a fresh dry run");

        var afterCount = await fixture.CountAsync("SELECT COUNT(*) FROM product WHERE code = 'VM-COMMIT-OK';");
        afterCount.Should().Be(1, "the commit actually reached the import service and wrote the row");

        // Proves the gate is real, not merely reset for show: a second commit attempt with the
        // stale (now false) flag is refused exactly as the never-previewed case above.
        var beforeSecond = await fixture.CountAsync("SELECT COUNT(*) FROM product;");
        await screen.CommitCommand.ExecuteAsync(null);
        screen.Status.Should().Be("Run a dry run with zero errors first.");
        (await fixture.CountAsync("SELECT COUNT(*) FROM product;")).Should().Be(beforeSecond,
            "committing twice in a row without a second dry run must not import the file twice");
    }

    private static ImportTabViewModel NewScreen(SaleFixture fixture) =>
        new(fixture.Resolve<ICatalogueImportService>(), fixture.Resolve<ISpreadsheetReader>());

    private string WriteCleanCsv(string fileName, string code) =>
        WriteCsv(fileName,
        [
            Row(code: code, name: "Clean VM Item", unit: "Piece", taxClass: "Exempt", price: "5.00", cost: "1.00", qty: "10"),
        ]);

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
        string unit,
        string taxClass,
        string price,
        string cost,
        string qty) =>
    [
        code, name, null, null, null, unit, null, taxClass, null,
        null, null, null, null, price, cost, qty,
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
