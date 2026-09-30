using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="ISalesSummaryReportQuery"/>: RPT-01 (task P3-T05 "Do this" #1).
/// </summary>
/// <remarks>
/// The headline totals are the canonical <see cref="SalesPeriodSummary"/> from
/// <see cref="PeriodFiguresReader.ReadTotalsAsync"/> (routed: rollups for closed dates, raw for the
/// open shift's); the breakdowns are <see cref="DailyFiguresReader"/>'s raw grouped reads through the
/// same <see cref="CanonicalFigures"/>. No cost column is read anywhere on this path - it never calls
/// <c>ReadWithCogsAsync</c> and asks the daily reader for no cost. Like the other queries, it builds
/// its own readers instead of taking the COGS-bearing engine by injection.
/// </remarks>
internal sealed class SalesSummaryReportQuery : ISalesSummaryReportQuery
{
    private readonly PeriodFiguresReader _period;
    private readonly DailyFiguresReader _daily;

    public SalesSummaryReportQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _period = new PeriodFiguresReader(connectionFactory);
        _daily = new DailyFiguresReader(connectionFactory);
    }

    /// <inheritdoc />
    public async Task<SalesSummaryReport> GetSummaryAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var totals = await _period.ReadTotalsAsync(range, ReportSourcePolicy.RollupsWhereClosed, cancellationToken)
            .ConfigureAwait(false);
        var days = await _daily.ReadDaysAsync(range, includeCost: false, cancellationToken).ConfigureAwait(false);
        var hours = await _daily.ReadHoursAsync(range, cancellationToken).ConfigureAwait(false);
        var tenders = await _daily.ReadTendersAsync(range, cancellationToken).ConfigureAwait(false);

        var summary = new SalesPeriodSummary(
            range,
            totals.BillCount,
            totals.ReturnCount,
            totals.GrossSales,
            totals.Discounts,
            totals.Tax,
            totals.NetSales,
            totals.ReturnsValue,
            totals.TenderTotal);

        return new SalesSummaryReport(
            range,
            summary,
            CanonicalFigures.AverageBillValue(totals.GrossSales, totals.Discounts, totals.BillCount),
            [
                .. days.Select(day => new SalesDayRow(
                    day.Date,
                    day.BillCount,
                    day.ReturnCount,
                    day.Gross,
                    day.Discounts,
                    day.Tax,
                    day.Net,
                    CanonicalFigures.AverageBillValue(day.Gross, day.Discounts, day.BillCount),
                    day.ReturnsValue)),
            ],
            [
                .. hours.Select(hour => new SalesHourRow(
                    hour.Hour,
                    hour.BillCount,
                    hour.Gross,
                    hour.Discounts,
                    hour.Tax,
                    hour.Net,
                    CanonicalFigures.AverageBillValue(hour.Gross, hour.Discounts, hour.BillCount))),
            ],
            [
                .. tenders.Select(tender => new SalesTenderRow(
                    tender.TenderType,
                    tender.SalesAmount,
                    tender.RefundsAmount,
                    tender.SalesAmount - tender.RefundsAmount)),
            ]);
    }
}
