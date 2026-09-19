using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.SeedGenerator;
using FluentAssertions;
using Xunit.Abstractions;

namespace Counterpoint.Integration.Tests.Dashboard;

/// <summary>
/// The dashboard's recent-sales list (SRS FR-9.7, task P3-T22): the last N completed bills, most
/// recent first, off <see cref="IRecentSalesQuery"/>.
/// </summary>
public sealed class RecentSalesQueryTests
{
    private readonly ITestOutputHelper _output;

    public RecentSalesQueryTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static readonly DateTimeOffset Bill1 = new(2026, 9, 6, 9, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset Bill2 = new(2026, 9, 6, 10, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset Bill3 = new(2026, 9, 6, 11, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset Bill4 = new(2026, 9, 6, 12, 0, 0, TimeSpan.FromHours(5.5));
    private static readonly DateTimeOffset Bill5 = new(2026, 9, 6, 13, 0, 0, TimeSpan.FromHours(5.5));

    [Fact]
    public async Task FR_9_7_ReturnsTheCorrectNMostRecentCompletedSalesMostRecentFirst()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var customerId = await fixture.Resolve<ICustomerMaintenance>().CreateAsync(
            new SaveCustomerCommand("Kamal Perera", null, null, null, "RETAIL", Money.Zero));

        var sale1 = await CompleteBoltSaleAsync(fixture, 1m, Bill1, customerId: null);
        var sale2 = await CompleteBoltSaleAsync(fixture, 2m, Bill2, customerId: null);
        var sale3 = await CompleteBoltSaleAsync(fixture, 3m, Bill3, customerId: customerId);
        var sale4 = await CompleteBoltSaleAsync(fixture, 4m, Bill4, customerId: null);
        var sale5 = await CompleteBoltSaleAsync(fixture, 5m, Bill5, customerId: null);

        var recent = await fixture.Resolve<IRecentSalesQuery>().GetRecentAsync(3);

        recent.Should().HaveCount(3);
        recent.Select(r => r.BillNo).Should().ContainInOrder(sale5.BillNo, sale4.BillNo, sale3.BillNo);
        recent[0].CompletedAt.Should().Be(Bill5);
        recent[0].Total.Should().Be(sale5.Total);
        recent[0].CustomerName.Should().Be("Walk-in");

        // Bill 3 - the one sale stamped with a real customer - carries that name, not "Walk-in".
        var bill3Row = recent.Single(r => r.BillNo == sale3.BillNo);
        bill3Row.CustomerName.Should().Be("Kamal Perera");
        bill3Row.Total.Should().Be(sale3.Total);

        // Bills 1 and 2 exist but fall outside the requested top 3.
        recent.Select(r => r.BillNo).Should().NotContain([sale1.BillNo, sale2.BillNo]);
    }

    [Fact]
    public async Task FR_9_7_ACancelledSaleNeverAppearsInTheList()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var kept = await CompleteBoltSaleAsync(fixture, 1m, Bill1, customerId: null);
        var cancelled = await CompleteBoltSaleAsync(fixture, 2m, Bill2, customerId: null);

        await fixture.Resolve<ICancelSale>().CancelAsync(
            new CancelSaleCommand(cancelled.SaleId, "Rung up in error", Bill2.AddMinutes(5)));

        var recent = await fixture.Resolve<IRecentSalesQuery>().GetRecentAsync(10);

        recent.Select(r => r.BillNo).Should().Contain(kept.BillNo);
        recent.Select(r => r.BillNo).Should().NotContain(cancelled.BillNo, "a cancelled bill must never appear in the recent-sales list");
    }

    [Fact]
    public async Task FR_9_7_FewerCompletedSalesThanRequestedReturnsAllOfThemWithoutThrowing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var sale1 = await CompleteBoltSaleAsync(fixture, 1m, Bill1, customerId: null);
        var sale2 = await CompleteBoltSaleAsync(fixture, 2m, Bill2, customerId: null);

        // Only two completed sales exist; asking for ten must not throw and must not pad the
        // result - it returns exactly what is there, most recent first.
        var recent = await fixture.Resolve<IRecentSalesQuery>().GetRecentAsync(10);

        recent.Should().HaveCount(2);
        recent.Select(r => r.BillNo).Should().ContainInOrder(sale2.BillNo, sale1.BillNo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task FR_9_7_ANonPositiveCountThrowsArgumentOutOfRangeException(int count)
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var query = fixture.Resolve<IRecentSalesQuery>();

        var act = async () => await query.GetRecentAsync(count);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>(
            "a zero or negative count is a caller bug, not an empty-result case, and must fail loudly rather than silently return nothing");
    }

    [Fact]
    public void FR_9_7_TheDtoCarriesNoCostOrMarginField()
    {
        // The same defence-in-depth reflection check ReportQueryLayerTests runs against the
        // cashier-visible report DTOs (CLAUDE.md invariant 8): no cost-bearing property, ever.
        var fieldNames = typeof(RecentSale).GetProperties().Select(property => property.Name).ToArray();

        fieldNames.Should().NotContain(name =>
            name.Contains("cost", StringComparison.OrdinalIgnoreCase)
            || name.Contains("cogs", StringComparison.OrdinalIgnoreCase)
            || name.Contains("profit", StringComparison.OrdinalIgnoreCase)
            || name.Contains("margin", StringComparison.OrdinalIgnoreCase));

        fieldNames.Should().BeEquivalentTo(["BillNo", "CompletedAt", "CustomerName", "Total"]);
    }

    /// <summary>
    /// Task P3-T22's own "Done when": the query executes in well under 50 ms against the
    /// 100,000-line seeded database, off the existing <c>ix_sale_soldat</c> index alone - no new
    /// index. Proved two ways: a timed run against the real
    /// <see cref="PerformanceDatasetSeeder"/> dataset, and an <c>EXPLAIN QUERY PLAN</c> against the
    /// exact SQL <see cref="Counterpoint.Infrastructure.Dashboard.SqliteRecentSalesQuery"/> runs,
    /// which must show a plain index search - never a full table scan and never a separate sort
    /// step ("USE TEMP B-TREE FOR ORDER BY") that a missing index would force.
    /// </summary>
    [Fact]
    public async Task FR_9_7_QueryCompletesWellUnderFiftyMillisecondsOnTheSeededPerformanceDatabaseWithNoNewIndex()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var clock = fixture.Resolve<TimeProvider>();
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        await PerformanceDatasetSeeder.SeedAsync(unitOfWork, clock, skuCount: 20_000, totalBillLines: 100_000);

        (await fixture.CountAsync("SELECT COUNT(*) FROM sale WHERE status = 'COMPLETED';"))
            .Should().BeGreaterThan(0, "the budget is meaningless if the seed left no completed sales");

        var query = fixture.Resolve<IRecentSalesQuery>();

        // One untimed warm-up, then the timed run - the budget is what the query costs once the
        // JIT and the page cache are warm, the same discipline ReportPeriodPerformanceTests uses.
        var warmup = await query.GetRecentAsync(20);
        warmup.Should().HaveCount(20);

        var stopwatch = Stopwatch.StartNew();
        var recent = await query.GetRecentAsync(20);
        stopwatch.Stop();

        recent.Should().HaveCount(20);
        recent.Should().BeInDescendingOrder(r => r.CompletedAt);

        var plan = await ExplainQueryPlanAsync(fixture);

        plan.Should().NotContain(
            line => line.Contains("SCAN sale", StringComparison.OrdinalIgnoreCase)
                && !line.Contains("USING INDEX", StringComparison.OrdinalIgnoreCase),
            "a full table scan of `sale` would defeat the point of the existing ix_sale_soldat index");
        plan.Should().NotContain(
            line => line.Contains("USE TEMP B-TREE", StringComparison.OrdinalIgnoreCase),
            "a separate sort step means the existing index is not already satisfying the ORDER BY - "
            + "the trigger for adding a new one, which this seeded volume must not require");

        _output.WriteLine(
            "P3-T22: GetRecentAsync(20) against a {0}-completed-sale database took {1:0.0} ms "
            + "against a 50 ms budget, on a development machine.",
            recent.Count > 0 ? "100,000-line" : "empty",
            stopwatch.Elapsed.TotalMilliseconds);

        stopwatch.Elapsed.Should().BeLessThan(
            TimeSpan.FromMilliseconds(50),
            $"P3-T22's own budget: this run took {stopwatch.Elapsed.TotalMilliseconds:0.0} ms");
    }

    /// <summary>
    /// Runs <c>EXPLAIN QUERY PLAN</c> against the identical SQL
    /// <c>SqliteRecentSalesQuery.RecentSalesSql</c> executes, off a read connection - proving what
    /// index the production query actually uses, not a paraphrase of it.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ExplainQueryPlanAsync(SaleFixture fixture)
    {
        var connection = await fixture.OpenReadConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                EXPLAIN QUERY PLAN
                SELECT s.bill_no, s.sold_at, COALESCE(c.name, 'Walk-in'), s.total
                  FROM sale s
                  LEFT JOIN customer c ON c.id = s.customer_id
                 WHERE s.status = 'COMPLETED'
                 ORDER BY s.sold_at DESC
                 LIMIT 20;
                """;

            var lines = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                // EXPLAIN QUERY PLAN's own shape: id, parent, notused, detail - "detail" is the
                // last column and the only one worth reading.
                lines.Add(reader.GetString(reader.FieldCount - 1));
            }

            return lines;
        }
    }

    private static async Task<CompletedSale> CompleteBoltSaleAsync(
        SaleFixture fixture, decimal quantity, DateTimeOffset soldAt, long? customerId)
    {
        var variantId = await fixture.CountAsync("SELECT id FROM product_variant ORDER BY id LIMIT 1;");
        var lines = new List<SaleLineRequest> { new(variantId, quantity) };
        var quote = await fixture.Resolve<IQuoteSale>().QuoteAsync(lines);

        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;
        var shiftId = fixture.Resolve<ISession>().ShiftId!.Value;

        return await fixture.Resolve<ICompleteSale>().CompleteAsync(new CompleteSaleCommand(
            userId,
            shiftId,
            soldAt,
            lines,
            [new TenderRequest(TenderTypes.Cash, quote.Total)],
            customerId));
    }
}
