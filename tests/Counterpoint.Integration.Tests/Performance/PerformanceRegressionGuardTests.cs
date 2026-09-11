using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.DependencyInjection;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.SeedGenerator;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Counterpoint.Integration.Tests.Performance;

/// <summary>
/// The software performance harness (P1-T16, SRS NFR-P1, NFR-P2, NFR-P3, NFR-P4, NFR-P6): the
/// seeded-database benchmark <c>/perf-gate</c> runs everywhere except the shop terminal.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a relative regression guard, not the absolute NFR-P1...P7 gate.</b> It fails the
/// build when a measurement drifts more than 20% from the figures recorded in
/// <c>docs/perf-regression-baseline.json</c> - never against the NFR budgets themselves, which
/// stay unmeasured in <c>docs/perf-baseline.md</c> until <c>HW-T07</c> records them on the shop's
/// own terminal (docs/09_HARDWARE_INTEGRATION.md). A dev machine or a CI runner is not the shop's
/// low-powered terminal, and NFR-P6 in particular is about that terminal specifically.
/// </para>
/// <para>
/// Measured against the same 20,000-SKU, 100,000-historical-bill-line database AC-18 and
/// <c>scripts/seed.sh</c> use (<see cref="PerformanceDatasetSeeder"/>, built once and shared
/// across every <c>[Fact]</c> in this class via <see cref="SeededDatabase"/>) - not an empty
/// database, and not the smaller ad hoc catalogues <c>CataloguePerformanceTests</c> seeds for its
/// own two NFR-P1/NFR-P2 benchmarks (P1-T06). Those already prove the budget is met against a
/// clean catalogue with no trading history; what this class adds is the trading history itself,
/// because that is where an unused or missing index only shows up (P1-T16's own "Risks": "scan
/// latency degrades with history").
/// </para>
/// <para>
/// NFR-P5 (a one-year report) and NFR-P7 (500,000+ lines) are not measured here: no report engine
/// exists yet (Phase 3) and 500k lines is HW-T07's absolute-budget territory, not this gate's.
/// </para>
/// </remarks>
[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class PerformanceRegressionGuardMarker
{
    /// <summary>Kept off the default parallel schedule - see <c>CataloguePerformanceTests</c>'s own remarks.</summary>
    internal const string CollectionName = "performance-regression-guard";
}

[Collection(PerformanceRegressionGuardMarker.CollectionName)]
public sealed class PerformanceRegressionGuardTests : IClassFixture<PerformanceRegressionGuardTests.SeededDatabase>
{
    private readonly SeededDatabase _database;

    public PerformanceRegressionGuardTests(SeededDatabase database)
    {
        _database = database;
    }

    [Fact]
    public async Task NFR_P1_BarcodeScanToLineAgainstTheAgedDatabase()
    {
        var elapsed = await BestOfThreeAsync(async () =>
        {
            var scanned = await _database.ScanItem.ScanAsync(PerformanceDatasetSeeder.BarcodeFor(10_000));
            scanned.Should().NotBeNull();
        });

        await AssertWithinBaselineAsync("NFR-P1", elapsed);
    }

    [Fact]
    public async Task NFR_P2_SearchResultsAgainstTheAgedDatabase()
    {
        var elapsed = await BestOfThreeAsync(async () =>
        {
            var results = await _database.Search.SearchAsync("Hardware");
            results.Should().NotBeEmpty();
        });

        await AssertWithinBaselineAsync("NFR-P2", elapsed);
    }

    [Fact]
    public async Task NFR_P3_BillCompletionAgainstTheAgedDatabase()
    {
        var lines = new List<SaleLineRequest> { new(_database.SampleVariantId, 1m) };

        var elapsed = await BestOfThreeAsync(async () =>
        {
            var quote = await _database.QuoteSale.QuoteAsync(lines);
            var completed = await _database.CompleteSale.CompleteAsync(new CompleteSaleCommand(
                _database.UserId,
                _database.ShiftId,
                TimeProvider.System.GetLocalNow(),
                lines,
                [new TenderRequest(TenderTypes.Cash, quote.Total)]));

            completed.SaleId.Should().BeGreaterThan(0);
        });

        await AssertWithinBaselineAsync("NFR-P3", elapsed);
    }

    [Fact]
    public async Task NFR_P4_BillLookupByNumberAgainstTheAgedDatabase()
    {
        // No Application-layer "find by bill number" port exists yet - the one screen that reads
        // a sale back (P1-T11's reprint) already knows the internal id by the time it asks. This
        // measures the query NFR-P4 actually describes - a lookup keyed by the printed bill
        // number - directly against ux_sale_bill_no, the same index a future lookup screen would
        // read through, without inventing that screen's Application port here (out of scope for
        // this task).
        const string billNo = "INV-2026-000001";

        var elapsed = await BestOfThreeAsync(async () =>
        {
            var connection = await _database.ConnectionFactory.OpenReadConnectionAsync();
            await using (connection.ConfigureAwait(false))
            {
                var saleId = await connection.QuerySingleOrDefaultAsync<long?>(
                    "SELECT id FROM sale WHERE bill_no = @BillNo;", new { BillNo = billNo });

                saleId.Should().NotBeNull();
            }
        });

        await AssertWithinBaselineAsync("NFR-P4", elapsed);
    }

    [Fact]
    public async Task NFR_P6_ColdStartProxyAgainstTheAgedDatabase()
    {
        // A proxy, not the real figure: NFR-P6 is "cold start to the sales screen" on the shop's
        // own low-powered terminal, which only HW-T07 can measure honestly (docs/perf-baseline.md
        // stays blank on this row until then). What is measured here - migrations already applied
        // (a no-op scan), FirstRunSeeder's idempotency check, and building the container - against
        // the real 20,000-SKU/100,000-line file this class seeded - is the same start-up path a
        // real cold start runs before the sales screen opens, so a regression here is still an
        // early warning worth having, even though the absolute number means nothing until it is
        // measured on the terminal.
        var elapsed = await BestOfThreeAsync(async () =>
        {
            await using var cold = await ColdStartFixture.OpenAsync(_database.Root);
        });

        await AssertWithinBaselineAsync("NFR-P6", elapsed);
    }

    private static async Task<TimeSpan> BestOfThreeAsync(Func<Task> operation)
    {
        // One untimed warm-up, then best of three - the same shape
        // CataloguePerformanceTests/LoginLatencyTests use: the question is what the operation
        // costs once the JIT and the page cache are warm, not what the first call's one-time tax
        // was.
        await operation();

        var best = TimeSpan.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var stopwatch = Stopwatch.StartNew();
            await operation();
            stopwatch.Stop();

            if (stopwatch.Elapsed < best)
            {
                best = stopwatch.Elapsed;
            }
        }

        return best;
    }

    private static async Task AssertWithinBaselineAsync(string operation, TimeSpan elapsed)
    {
        var baseline = await PerformanceBaseline.LoadAsync();
        var recordedMs = baseline.MillisecondsFor(operation);

        var allowedMs = recordedMs * 1.2;

        elapsed.TotalMilliseconds.Should().BeLessThanOrEqualTo(
            allowedMs,
            $"{operation} took {elapsed.TotalMilliseconds:0.0} ms, more than 20% over the "
            + $"{recordedMs:0.0} ms recorded in docs/perf-regression-baseline.json - fix the "
            + "regression (or, if the change is deliberate, update the recorded baseline by hand)");
    }

    /// <summary>
    /// Builds the 20,000-SKU/100,000-line database once and shares it across every <c>[Fact]</c>
    /// in this class (xunit's per-class fixture lifetime) - reseeding it five times would spend
    /// most of the run seeding, not measuring.
    /// </summary>
    public sealed class SeededDatabase : IAsyncLifetime
    {
        private SaleFixture? _fixture;

        internal string Root { get; private set; } = string.Empty;

        internal IScanItem ScanItem => _fixture!.Resolve<IScanItem>();

        internal IProductSearchService Search => _fixture!.Resolve<IProductSearchService>();

        internal ICompleteSale CompleteSale => _fixture!.Resolve<ICompleteSale>();

        internal IQuoteSale QuoteSale => _fixture!.Resolve<IQuoteSale>();

        internal IPosConnectionFactory ConnectionFactory => _fixture!.Resolve<IPosConnectionFactory>();

        internal long UserId { get; private set; }

        internal long ShiftId { get; private set; }

        internal long SampleVariantId { get; private set; }

        public async Task InitializeAsync()
        {
            _fixture = await SaleFixture.CreateSignedInAsync();

            await PerformanceDatasetSeeder.SeedAsync(
                _fixture.Resolve<SqliteUnitOfWork>(),
                _fixture.Resolve<TimeProvider>(),
                skuCount: 20_000,
                totalBillLines: 100_000);

            UserId = _fixture.Resolve<ISession>().CurrentUser!.Id;
            ShiftId = Convert.ToInt64(
                await _fixture.ScalarAsync("SELECT id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;"),
                CultureInfo.InvariantCulture);
            SampleVariantId = Convert.ToInt64(
                await _fixture.ScalarAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;"),
                CultureInfo.InvariantCulture);

            Root = _fixture.Root;
        }

        public async Task DisposeAsync()
        {
            if (_fixture is not null)
            {
                await _fixture.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// A second, independent container over the same already-migrated, already-seeded database -
    /// the same shape a real cold start builds, minus Avalonia (which cannot open a window in CI).
    /// </summary>
    private sealed class ColdStartFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private ColdStartFixture(ServiceProvider services)
        {
            _services = services;
        }

        internal static async Task<ColdStartFixture> OpenAsync(string root)
        {
            var services = new ServiceCollection();
            services.AddSingleton(TimeProvider.System);
            services.AddCounterpointInfrastructure(root);

            var provider = services.BuildServiceProvider();
            var fixture = new ColdStartFixture(provider);

            await provider.GetRequiredService<MigrationRunner>().ApplyPendingMigrationsAsync();
            await provider.GetRequiredService<FirstRunSeeder>().EnsureSeededAsync();

            return fixture;
        }

        public async ValueTask DisposeAsync() => await _services.DisposeAsync();
    }
}
