using System;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Sales;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// Runs P1-T06's two "Done when" benchmarks on their own, with the rest of the suite held back.
/// </summary>
/// <remarks>
/// Same reasoning as <c>LoginLatencyTests</c>: seeding 20,000-50,000 rows and then timing a
/// single query is deliberately CPU- and IO-bound, and a number produced while a dozen other
/// test classes are competing for the same cores is a fact about the scheduler, not about the
/// query.
/// </remarks>
[CollectionDefinition(CataloguePerformanceTests.CollectionName, DisableParallelization = true)]
public sealed class CataloguePerformanceMarker
{
}

/// <summary>
/// The two benchmark "Done when" rows of P1-T06 (docs/03_PHASE_1_core_trading.md, SRS NFR-P1,
/// NFR-P2) - not asserted anywhere else in the suite, unlike the functional rows of the same
/// list, which <see cref="BarcodeMaintenanceTests"/> and <see cref="ProductSearchServiceTests"/>
/// already cover.
/// </summary>
/// <remarks>
/// <para>
/// Seeded with raw batched SQL in one transaction, never one <c>INSERT</c> per row and never
/// through <see cref="IProductMaintenance"/>: SQLite's cost here is almost entirely the number of
/// transactions committed (each one an <c>fsync</c> under <c>synchronous=FULL</c>, CLAUDE.md
/// invariant 9), so a single transaction of a few thousand-row <c>INSERT</c> statements seeds
/// tens of thousands of rows in a small fraction of a second while a real till would take minutes
/// to load a catalogue that size one product at a time. The measured operation afterwards is
/// exactly the same query <see cref="IScanItem"/> and <see cref="IProductSearchService"/> run for
/// a cashier - the seeding shortcut changes nothing about what gets timed.
/// </para>
/// <para>
/// Best of three, after one untimed warm-up call, the same shape
/// <c>LoginLatencyTests.LoginCompletesWellUnderHalfASecondWithTheShippedWorkFactors</c> uses: the
/// question is what the query costs, not what the shared build agent's first-JIT tax was.
/// </para>
/// </remarks>
[Collection(CollectionName)]
public sealed class CataloguePerformanceTests
{
    internal const string CollectionName = "catalogue-performance";

    private const int BarcodeBenchmarkSkuCount = 20_000;
    private const int SearchBenchmarkSkuCount = 50_000;
    private const int SeedBatchSize = 2_000;
    private const string SeedTimestamp = "2026-09-04T08:00:00.000+05:30";

    [Fact]
    public async Task NFR_P1_BarcodeLookupReturnsInUnder300MsEndToEndWith20000Skus()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (baseUomId, taxClassId) = await ReferenceIdsAsync(fixture);
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        var targetIndex = BarcodeBenchmarkSkuCount / 2;
        var targetBarcode = BarcodeFor(targetIndex);

        await unitOfWork.ExecuteInTransactionAsync<object?>(async (connection, transaction, token) =>
        {
            await SeedProductsAndVariantsAsync(
                connection, transaction, BarcodeBenchmarkSkuCount, baseUomId, taxClassId, includeLocation: false);
            await SeedBarcodesAsync(connection, transaction, BarcodeBenchmarkSkuCount);
            return null;
        });

        // IScanItem, not IProductLookup directly: this is the Application-layer boundary a scan
        // at the till actually crosses (SRS NFR-P1 "barcode scan to line on the bill"), one level
        // above the raw Dapper query SqliteProductLookup runs.
        var scan = fixture.Resolve<IScanItem>();

        var warmUp = await scan.ScanAsync(targetBarcode);
        warmUp.Should().NotBeNull("the seeded barcode must resolve, or the timing below proves nothing");

        var best = TimeSpan.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            var scanned = await scan.ScanAsync(targetBarcode);
            stopwatch.Stop();

            scanned.Should().NotBeNull();

            if (stopwatch.Elapsed < best)
            {
                best = stopwatch.Elapsed;
            }
        }

        best.Should().BeLessThan(
            TimeSpan.FromMilliseconds(300),
            "NFR-P1: a barcode scan must resolve to a bill line in under 300 ms with 20,000 SKUs seeded");
    }

    [Fact]
    public async Task NFR_P2_SearchResultsReturnInUnder500MsWith50000Skus()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var (baseUomId, taxClassId) = await ReferenceIdsAsync(fixture);
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync<object?>(async (connection, transaction, token) =>
        {
            await SeedProductsAndVariantsAsync(
                connection, transaction, SearchBenchmarkSkuCount, baseUomId, taxClassId, includeLocation: true);
            return null;
        });

        var search = fixture.Resolve<IProductSearchService>();

        // "Widget" is a token of every one of the 50,000 seeded names, so this is the worst case
        // for the LIMIT-50 path a keystroke actually exercises: FTS5 must find and rank far more
        // candidates than it returns, not just the fast case of a rare, near-unique fragment.
        const string query = "Widget";

        var warmUp = await search.SearchAsync(query);
        warmUp.Should().NotBeEmpty("the seeded catalogue must be searchable, or the timing below proves nothing");
        warmUp.Should().HaveCountLessThanOrEqualTo(50, "FR-2.11 caps a result page at 50 rows");

        var best = TimeSpan.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            var results = await search.SearchAsync(query);
            stopwatch.Stop();

            results.Should().NotBeEmpty();

            if (stopwatch.Elapsed < best)
            {
                best = stopwatch.Elapsed;
            }
        }

        best.Should().BeLessThan(
            TimeSpan.FromMilliseconds(500),
            "NFR-P2: search must return within 500 ms of the last keystroke with 50,000 SKUs seeded");
    }

    /// <summary>The seeded owner has done nothing to the catalogue yet, so "Piece" and "Exempt" are still the reference rows every other catalogue test resolves by name.</summary>
    private static async Task<(long BaseUomId, long TaxClassId)> ReferenceIdsAsync(SaleFixture fixture)
    {
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var baseUomId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var taxClassId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        return (baseUomId, taxClassId);
    }

    /// <summary>
    /// One product and one variant per index, batched into multi-row <c>INSERT</c> statements
    /// inside the caller's transaction. Product ids and variant ids each occupy their own high
    /// range so they can never collide with a seeded reference row or with each other's table.
    /// </summary>
    private static async Task SeedProductsAndVariantsAsync(
        DbConnection connection,
        DbTransaction transaction,
        int count,
        long baseUomId,
        long taxClassId,
        bool includeLocation)
    {
        const long ProductIdBase = 1_000_000;
        const long VariantIdBase = 2_000_000;

        var productColumns = includeLocation
            ? "(id, code, name, base_uom_id, type, tax_class_id, location, created_at, updated_at)"
            : "(id, code, name, base_uom_id, type, tax_class_id, created_at, updated_at)";

        await ExecuteBatchedAsync(
            connection,
            transaction,
            "INSERT INTO product " + productColumns + " VALUES ",
            count,
            SeedBatchSize,
            i =>
            {
                var id = ProductIdBase + i;
                var code = "PERF-" + i.ToString("000000", CultureInfo.InvariantCulture);
                var name = "Performance Widget " + i.ToString("000000", CultureInfo.InvariantCulture);

                return includeLocation
                    ? $"({id},'{code}','{name}',{baseUomId},'STANDARD',{taxClassId},'A1','{SeedTimestamp}','{SeedTimestamp}')"
                    : $"({id},'{code}','{name}',{baseUomId},'STANDARD',{taxClassId},'{SeedTimestamp}','{SeedTimestamp}')";
            });

        await ExecuteBatchedAsync(
            connection,
            transaction,
            "INSERT INTO product_variant (id, product_id, sku, price, created_at) VALUES ",
            count,
            SeedBatchSize,
            i =>
            {
                var id = VariantIdBase + i;
                var productId = ProductIdBase + i;
                var sku = "PERF-" + i.ToString("000000", CultureInfo.InvariantCulture) + "-A";

                // Money, scaled x10000 (CLAUDE.md invariant 1): 50000 is $5.00.
                return $"({id},{productId},'{sku}',50000,'{SeedTimestamp}')";
            });
    }

    /// <summary>One primary barcode per already-seeded variant, keyed by the same index.</summary>
    private static async Task SeedBarcodesAsync(DbConnection connection, DbTransaction transaction, int count)
    {
        const long BarcodeIdBase = 3_000_000;
        const long VariantIdBase = 2_000_000;

        await ExecuteBatchedAsync(
            connection,
            transaction,
            "INSERT INTO barcode (id, product_variant_id, barcode, is_primary) VALUES ",
            count,
            SeedBatchSize,
            i =>
            {
                var id = BarcodeIdBase + i;
                var variantId = VariantIdBase + i;

                return $"({id},{variantId},'{BarcodeFor(i)}',1)";
            });
    }

    /// <summary>A 13-digit numeric code unique across the seeded range, EAN-13 shaped but not check-digit valid - this is a volume fixture, not <see cref="InternalBarcodeGeneratorTests"/>.</summary>
    private static string BarcodeFor(int index) => "9" + index.ToString("000000000000", CultureInfo.InvariantCulture);

    /// <summary>
    /// Runs <paramref name="rowSql"/> for every index in <c>[0, count)</c>, batched into
    /// multi-row <c>INSERT ... VALUES (...), (...), ...</c> statements of at most
    /// <paramref name="batchSize"/> rows each, all inside the caller's already-open transaction.
    /// One transaction, not one round trip per row and not one <c>INSERT</c> per row - the two
    /// costs CLAUDE.md's seeding guidance calls out.
    /// </summary>
    private static async Task ExecuteBatchedAsync(
        DbConnection connection,
        DbTransaction transaction,
        string insertPrefix,
        int count,
        int batchSize,
        Func<int, string> rowSql)
    {
        var builder = new StringBuilder();
        var rowsInBatch = 0;

        for (var i = 0; i < count; i++)
        {
            if (rowsInBatch == 0)
            {
                builder.Clear();
                builder.Append(insertPrefix);
            }
            else
            {
                builder.Append(',');
            }

            builder.Append(rowSql(i));
            rowsInBatch++;

            if (rowsInBatch == batchSize || i == count - 1)
            {
                builder.Append(';');

                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = builder.ToString();
                await command.ExecuteNonQueryAsync();

                rowsInBatch = 0;
            }
        }
    }
}
