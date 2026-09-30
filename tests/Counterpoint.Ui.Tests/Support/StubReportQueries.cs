using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Ui.Tests.Support;

/// <summary>
/// In-memory stand-ins for the five P3-T05 report queries, so a report screen can be rendered in a
/// headless window with known figures and no database. What is under test in the view tests is the
/// binding and the navigation, never the figures - <c>Counterpoint.Integration.Tests</c> proves
/// those against a real SQLite file.
/// </summary>
internal static class StubReports
{
    internal static readonly DateOnly Day = new(2026, 9, 6);

    internal static Money M(decimal amount) => Money.FromDecimal(amount);

    internal static SalesBillRow Bill(long id, string billNo) => new(
        id, billNo, new DateTimeOffset(2026, 9, 6, 10, 5, 0, TimeSpan.FromHours(5.5)), Day,
        "Walk-in", "Shop Owner", M(550m), M(56m), M(49.40m), M(494m), M(543.40m), Money.Zero);

    internal static SalesBillDetail Detail(long id, string billNo) => new(
        id, billNo, new DateTimeOffset(2026, 9, 6, 10, 5, 0, TimeSpan.FromHours(5.5)), Day, "COMPLETED",
        "Shop Owner", "Walk-in", M(520m), M(30m), M(26m), M(49.40m), Money.Zero, M(543.40m), null,
        [new SalesBillLine(1, "Bolt", Quantity.FromDecimal(3m, 1), "pc", M(100m), M(30m), M(25.65m), M(270m), Quantity.Zero(0))],
        [new SalesBillPayment("CASH", M(543.40m))],
        []);
}

internal sealed class StubSalesSummaryQuery : ISalesSummaryReportQuery
{
    public Task<SalesSummaryReport> GetSummaryAsync(ReportDateRange range, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesSummaryReport(
            range,
            new SalesPeriodSummary(
                range, 3, 1, StubReports.M(1000m), StubReports.M(56m), StubReports.M(69.40m),
                StubReports.M(944m), StubReports.M(94.05m), StubReports.M(1013.40m)),
            StubReports.M(314.67m),
            [new SalesDayRow(StubReports.Day, 3, 1, StubReports.M(1000m), StubReports.M(56m), StubReports.M(69.40m),
                StubReports.M(944m), StubReports.M(314.67m), StubReports.M(94.05m))],
            [new SalesHourRow(10, 2, StubReports.M(800m), StubReports.M(56m), StubReports.M(49.40m), StubReports.M(744m), StubReports.M(372m))],
            [new SalesTenderRow("CASH", StubReports.M(650m), StubReports.M(94.05m), StubReports.M(555.95m))]));
}

internal sealed class StubBillQuery : ISalesBillQuery
{
    internal bool Truncate { get; set; }

    internal BillListFilter? LastFilter { get; private set; }

    internal long? LastOpened { get; private set; }

    public Task<SalesBillList> GetBillsAsync(BillListFilter filter, CancellationToken cancellationToken = default)
    {
        LastFilter = filter;

        return Task.FromResult(new SalesBillList(
            [StubReports.Bill(11, "INV-2026-000001"), StubReports.Bill(12, "INV-2026-000002")], Truncate));
    }

    public Task<SalesBillDetail?> GetBillAsync(long saleId, CancellationToken cancellationToken = default)
    {
        LastOpened = saleId;

        return Task.FromResult<SalesBillDetail?>(saleId == 404 ? null : StubReports.Detail(saleId, "INV-2026-00000" + saleId % 10));
    }
}

internal sealed class StubBreakdownQuery : ISalesBreakdownQuery
{
    public Task<SalesBreakdownReport> GetBreakdownAsync(
        ReportDateRange range, SalesBreakdownDimension dimension, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SalesBreakdownReport(
            range,
            dimension,
            StubReports.M(487.50m),
            [new SalesBreakdownRow(1, 7, "RPT-DRILL-A", "Drill", Quantity.FromDecimal(2m, 1), "pc", StubReports.M(487.50m), 1m)]));
}

internal sealed class StubProfitQuery : IProfitReportQuery
{
    public Task<ProfitReport> GetProfitReportAsync(
        ReportDateRange range, ProfitGrouping grouping, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProfitReport(
            range,
            grouping,
            new ProfitPeriodSummary(
                range, 3, 1, StubReports.M(1000m), StubReports.M(56m), StubReports.M(69.40m), StubReports.M(944m),
                StubReports.M(94.05m), StubReports.M(1013.40m), StubReports.M(546m), StubReports.M(398m), 398m / 944m),
            [new ProfitRow(
                grouping is ProfitGrouping.Day or ProfitGrouping.Month ? null : 1,
                grouping is ProfitGrouping.Day or ProfitGrouping.Month ? null : 7,
                grouping == ProfitGrouping.Item ? "RPT-DRILL-A" : string.Empty,
                grouping == ProfitGrouping.Day ? "2026-09-06" : "Drill",
                Quantity.FromDecimal(2m, 1),
                "pc",
                StubReports.M(487.50m),
                StubReports.M(450m),
                StubReports.M(37.50m),
                37.5m / 487.5m,
                1m,
                grouping is ProfitGrouping.Day or ProfitGrouping.Month
                    ? ReportDateRange.Custom(StubReports.Day, StubReports.Day)
                    : null)]));
}

internal sealed class StubReturnsQuery : IReturnsReportQuery
{
    public Task<ReturnsReport> GetReturnsReportAsync(ReportDateRange range, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ReturnsGroupRow> rows =
        [
            new ReturnsGroupRow("Cracked housing", 1, Quantity.FromDecimal(1m, 0), StubReports.M(250m), 1m),
        ];

        return Task.FromResult(new ReturnsReport(
            range, 3, StubReports.M(435.50m), StubReports.M(479.05m), StubReports.M(1594m), 5, 435.5m / 1594m, 0.6m,
            rows, rows, rows, rows, rows));
    }
}
