using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using Counterpoint.SeedGenerator;
using FluentAssertions;
using Xunit.Abstractions;

namespace Counterpoint.Integration.Tests.Reporting;

/// <summary>
/// Task P3-T04's own "Done when": a one-year report run completes in under 10 seconds (SRS
/// NFR-P5, "any report over a one-year range completes in under 10 seconds").
/// </summary>
/// <remarks>
/// <para>
/// Measured against the same 20,000-SKU, 100,000-historical-bill-line database the Phase 1
/// regression guard and <c>scripts/seed.sh</c> build (<see cref="PerformanceDatasetSeeder"/>) -
/// the only dataset in the repo with a year's worth of trading history behind it, which is what
/// NFR-P5's budget is actually about. The seed itself is deliberately <em>not</em> inside the timed
/// window: the budget is for the report, not for building the shop's history.
/// </para>
/// <para>
/// The 60 historical business dates are rolled up first, exactly as 60 shift closes would have left
/// them (P3-T03), so the timed query runs the routing's production shape - rollups for closed dates,
/// raw tables around the open shift. The same range is then read with
/// <see cref="ReportSourcePolicy.RawTablesRequired"/> and the two must agree, which is what makes
/// the budget a statement about the real report rather than about a query that quietly returned
/// nothing.
/// </para>
/// <para>
/// This is a Linux-CI figure on a development machine, not the shop terminal's own - the absolute
/// NFR-P5 row in <c>docs/perf-baseline.md</c> stays blank until <c>HW-T07</c> records it there
/// (docs/09_HARDWARE_INTEGRATION.md). A regression here is still an early warning worth having.
/// </para>
/// </remarks>
public sealed class ReportPeriodPerformanceTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

    private readonly ITestOutputHelper _output;

    public ReportPeriodPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task NFR_P5_AOneYearReportRunsUnderTenSecondsOnThePerformanceDataset()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();

        var clock = fixture.Resolve<TimeProvider>();
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var rollupBuilder = fixture.Resolve<IDailyRollupBuilder>();

        await PerformanceDatasetSeeder.SeedAsync(unitOfWork, clock, skuCount: 20_000, totalBillLines: 100_000);

        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        var builtAt = clock.GetLocalNow();

        // The trailing 60 business days the seeder traded, in the same order a shop would have
        // closed them.
        for (var dayIndex = 0; dayIndex < PerformanceDatasetSeeder.HistoricalDayCount; dayIndex++)
        {
            var businessDate = today.AddDays(-(PerformanceDatasetSeeder.HistoricalDayCount - dayIndex));
            await rollupBuilder.RebuildAsync(businessDate, builtAt);
        }

        (await fixture.CountAsync("SELECT COUNT(*) FROM daily_sales_summary;"))
            .Should().Be(
                PerformanceDatasetSeeder.HistoricalDayCount,
                "the closed year's every trading day must have a rollup for this to measure the routed query");

        // One whole year, ending today.
        var range = ReportDateRange.Custom(today.AddYears(-1).AddDays(1), today);
        var query = fixture.Resolve<ISalesPeriodSummaryQuery>();

        // One untimed warm-up, then the timed run: the budget is what the report costs once the JIT
        // and the page cache are warm, not the one-time cost of the first query on this connection.
        var warmup = await query.GetSalesSummaryAsync(range);
        warmup.BillCount.Should().BeGreaterThan(0, "the budget is meaningless if the report returned nothing");

        var routedStopwatch = Stopwatch.StartNew();
        var routed = await query.GetSalesSummaryAsync(range);
        routedStopwatch.Stop();

        var raw = await query.GetSalesSummaryAsync(range, ReportSourcePolicy.RawTablesRequired);

        routed.Should().Be(
            raw,
            "the routed one-year report must equal the same range read from raw tables only "
            + "(docs/report-definitions.md: the routing is a performance switch, never a correctness one)");

        routed.NetSales.Should().BeGreaterThan(Money.Zero);
        routed.TenderTotal.Should().BeGreaterThan(Money.Zero);

        _output.WriteLine(
            "NFR-P5: one-year routed report over {0} bills (net {1}) took {2:0.000} s "
            + "against a 10 s budget, on a development machine - the shop terminal's own figure is HW-T07's.",
            routed.BillCount,
            routed.NetSales,
            routedStopwatch.Elapsed.TotalSeconds);

        routedStopwatch.Elapsed.Should().BeLessThan(
            Budget,
            $"NFR-P5: a one-year report must complete in under 10 seconds on the seeded database; "
            + $"this run took {routedStopwatch.Elapsed.TotalSeconds:0.00} s");
    }
}
