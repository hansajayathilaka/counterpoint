using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Import;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Import;

/// <summary>
/// A blank/unmapped Cost column on a re-import must never silently drag an existing product's
/// moving-average cost toward zero (P1-T13 follow-up defect - money correctness, CLAUDE.md
/// invariant 3: <c>StockLedger.PostAsync</c> blends whatever unit cost it is handed into
/// <c>stock_balance.cost_avg</c> on every inbound movement, <c>MovingAverageCost.Recompute</c>).
/// </summary>
public sealed class CatalogueImportCostPreservationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "counterpoint-import-cost-tests", Guid.NewGuid().ToString("N"));

    public CatalogueImportCostPreservationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task ReimportWithABlankCostColumnPreservesAnEstablishedMovingAverageCost()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var import = fixture.Resolve<ICatalogueImportService>();

        const string code = "COST-01";

        // 1. Create the product with an opening cost of 4.00, ten units - CommitAsync's own
        //    Create path posts the first inbound movement, so cost_avg starts at exactly 4.00.
        var createFile = WriteCsv(
            "create.csv",
            [Row(code, "Cost Preservation Item", "Piece", "Exempt", price: "20.00", cost: "4.00", qty: "10")]);

        var created = await import.CommitAsync(createFile, ImportColumnMapping.Default);
        created.Counts.Creates.Should().Be(1);

        var variantId = await fixture.CountAsync(
            $"SELECT pv.id FROM product p JOIN product_variant pv ON pv.product_id = p.id WHERE p.code = '{code}';");

        // 2. Post a second, genuinely different-cost receipt directly through the real
        //    IStockLedger (the pattern P1-T07's RebuildStockBalanceCommandTests uses) - ten more
        //    units at 8.00 each - so the resulting 6.00 average is a real blend, not just the
        //    creation cost carried through untouched. This is the non-zero moving average the
        //    defect says a blank-cost re-import corrupts.
        var ledger = fixture.Resolve<IStockLedger>();
        var userId = await fixture.CountAsync("SELECT id FROM app_user ORDER BY id LIMIT 1;");
        var baseUomId = await fixture.CountAsync("SELECT base_uom_id FROM product WHERE code = '" + code + "';");

        await ledger.PostAsync(new StockPosting(
            variantId,
            "GRN",
            Quantity.FromDecimal(10m, baseUomId),
            Money.FromDecimal(8.00m),
            "GRN",
            RefDocId: null,
            userId,
            new DateTimeOffset(2026, 9, 9, 9, 0, 0, TimeSpan.FromHours(5.5))));

        var costAvgBefore = await fixture.ScalarAsync(
            $"SELECT cost_avg FROM stock_balance WHERE product_variant_id = {variantId};");
        costAvgBefore.Should().Be("60000", "(10 x 4.00 + 10 x 8.00) / 20 = 6.00, scaled x10000");

        // 3. Re-import the same code with an increased quantity and a blank Cost cell - the
        //    routine "just update quantities" re-import the defect describes. The row is
        //    otherwise entirely valid: it must commit, not error.
        var reimportFile = WriteCsv(
            "reimport.csv",
            [Row(code, "Cost Preservation Item", "Piece", "Exempt", price: "20.00", cost: "", qty: "30")]);

        var preview = await import.PreviewAsync(reimportFile, ImportColumnMapping.Default);
        preview.Counts.Errors.Should().Be(0, "a blank Cost cell on an update is not a validation error");
        preview.Counts.Updates.Should().Be(1);

        var reimported = await import.CommitAsync(reimportFile, ImportColumnMapping.Default);
        reimported.Counts.Updates.Should().Be(1);
        reimported.Counts.Errors.Should().Be(0);

        var qtyAfter = await fixture.ScalarAsync(
            $"SELECT qty_base FROM stock_balance WHERE product_variant_id = {variantId};");
        qtyAfter.Should().Be("300000", "30 scaled x10000 - the quantity update itself must still take effect");

        var costAvgAfter = await fixture.ScalarAsync(
            $"SELECT cost_avg FROM stock_balance WHERE product_variant_id = {variantId};");
        costAvgAfter.Should().Be(
            costAvgBefore,
            "a blank Cost column must fall back to the variant's current moving-average cost, "
            + "not be treated as an inbound unit cost of zero - posting zero here would have "
            + "recomputed the average to (20 x 6.00 + 10 x 0) / 30 = 4.00, silently corrupting it");

        // The stock movement the re-import posted must itself carry the fallback cost, not zero -
        // the ledger is append-only truth (CLAUDE.md invariant 5), so a wrong unit_cost here is
        // exactly the corruption a rebuild (P1-T07's IRebuildStockBalance) would faithfully replay.
        var lastMovementCost = await fixture.ScalarAsync(
            $"SELECT unit_cost FROM stock_movement WHERE product_variant_id = {variantId} "
            + "ORDER BY id DESC LIMIT 1;");
        lastMovementCost.Should().Be("60000", "the OPENING movement this re-import posted must record the fallback cost, not zero");
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

    /// <summary>One row, in <see cref="CatalogueExportColumns.Headers"/> order - a narrowed copy of
    /// <c>CatalogueImportTests.Row</c>'s shape, with only the columns this test needs.</summary>
    private static string?[] Row(string code, string name, string unit, string taxClass, string price, string cost, string qty) =>
    [
        code, name, /* nameAlt */ null, /* category */ null, /* brand */ null, unit,
        /* type */ null, taxClass, /* location */ null, /* nonReturnable */ null,
        /* warrantyDays */ null, /* notes */ null, /* barcode */ null, price, cost, qty,
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
