using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="IProfitReportQuery"/>: RPT-03 (task P3-T05 "Do this" #3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only by construction.</b> The interface carries <c>[RequiresRole(Role.Owner)]</c>; this
/// class is <c>internal</c> and is registered only wrapped with <c>RoleAuthorisation</c> (see
/// <c>ReportingServiceCollectionExtensions</c>), so no container path yields an undecorated one -
/// the same discipline <see cref="ProfitPeriodSummaryQuery"/> keeps. It is the one place besides
/// that query allowed to read a cost column.
/// </para>
/// <para>
/// <b>Reuse, not redefinition.</b> The headline <see cref="ProfitReport.Totals"/> is
/// <see cref="ProfitPeriodSummaryQuery"/> itself. Day/month rows are <see cref="DailyFiguresReader"/>
/// (snapshot <c>sale.cogs</c> less SELLABLE return cost); item/category/brand rows are
/// <see cref="ItemFiguresReader"/> (snapshot <c>sale_line.unit_cost</c> x <c>qty_base</c> less the same).
/// Gross profit and margin rate come from <see cref="CanonicalFigures"/>. Nothing reads
/// <c>product.cost_avg</c>, so changing a product's cost later moves no past period's profit.
/// </para>
/// </remarks>
internal sealed class ProfitReportQuery : IProfitReportQuery
{
    private readonly ProfitPeriodSummaryQuery _totals;
    private readonly DailyFiguresReader _daily;
    private readonly ItemFiguresReader _items;

    public ProfitReportQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _totals = new ProfitPeriodSummaryQuery(connectionFactory);
        _daily = new DailyFiguresReader(connectionFactory);
        _items = new ItemFiguresReader(connectionFactory);
    }

    /// <inheritdoc />
    public async Task<ProfitReport> GetProfitReportAsync(
        ReportDateRange range,
        ProfitGrouping grouping,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var totals = await _totals.GetProfitSummaryAsync(range, ReportSourcePolicy.RollupsWhereClosed, cancellationToken)
            .ConfigureAwait(false);

        var rows = grouping switch
        {
            ProfitGrouping.Day => await DayRowsAsync(range, cancellationToken).ConfigureAwait(false),
            ProfitGrouping.Month => await MonthRowsAsync(range, cancellationToken).ConfigureAwait(false),
            ProfitGrouping.Category => await GroupRowsAsync(range, SalesBreakdownDimension.Category, cancellationToken).ConfigureAwait(false),
            ProfitGrouping.Brand => await GroupRowsAsync(range, SalesBreakdownDimension.Brand, cancellationToken).ConfigureAwait(false),
            ProfitGrouping.Item => await GroupRowsAsync(range, SalesBreakdownDimension.Item, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(grouping), grouping, "Unknown profit grouping."),
        };

        return new ProfitReport(range, grouping, totals, rows);
    }

    private async Task<IReadOnlyList<ProfitRow>> DayRowsAsync(ReportDateRange range, CancellationToken cancellationToken)
    {
        var days = await _daily.ReadDaysAsync(range, includeCost: true, cancellationToken).ConfigureAwait(false);
        var totalNet = CanonicalFigures.Sum(days.Select(day => day.Net));

        return
        [
            .. days.Select(day => PeriodRow(
                day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                day.Net,
                day.Cogs,
                totalNet,
                ReportDateRange.Custom(day.Date, day.Date))),
        ];
    }

    private async Task<IReadOnlyList<ProfitRow>> MonthRowsAsync(ReportDateRange range, CancellationToken cancellationToken)
    {
        var days = await _daily.ReadDaysAsync(range, includeCost: true, cancellationToken).ConfigureAwait(false);
        var totalNet = CanonicalFigures.Sum(days.Select(day => day.Net));

        return
        [
            .. days
                .GroupBy(day => (day.Date.Year, day.Date.Month))
                .OrderBy(month => month.Key.Year)
                .ThenBy(month => month.Key.Month)
                .Select(month =>
                {
                    var first = new DateOnly(month.Key.Year, month.Key.Month, 1);
                    var last = first.AddMonths(1).AddDays(-1);

                    return PeriodRow(
                        first.ToString("yyyy-MM", CultureInfo.InvariantCulture),
                        CanonicalFigures.Sum(month.Select(day => day.Net)),
                        CanonicalFigures.Sum(month.Select(day => day.Cogs)),
                        totalNet,

                        // Clipped to the report's own range, so drilling in never lists a bill the report excluded.
                        ReportDateRange.Custom(
                            first < range.From ? range.From : first,
                            last > range.To ? range.To : last));
                }),
        ];
    }

    private async Task<IReadOnlyList<ProfitRow>> GroupRowsAsync(
        ReportDateRange range,
        SalesBreakdownDimension dimension,
        CancellationToken cancellationToken)
    {
        var figures = await _items.ReadAsync(range, dimension, includeCost: true, cancellationToken).ConfigureAwait(false);
        var totalNet = CanonicalFigures.Sum(figures.Select(figure => figure.Net));

        var ordered = figures
            .Select(figure => (Figure: figure, Profit: CanonicalFigures.GrossProfit(figure.Net, figure.Cogs)))
            .OrderByDescending(entry => entry.Profit.Amount)
            .ThenByDescending(entry => entry.Figure.Net.Amount)
            .ThenBy(entry => entry.Figure.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.Figure.Key)
            .ToList();

        return
        [
            .. ordered.Select((entry, index) => new ProfitRow(
                index + 1,
                entry.Figure.Key,
                entry.Figure.Code,
                entry.Figure.Name,
                entry.Figure.QtyBase,
                entry.Figure.UomSymbol,
                entry.Figure.Net,
                entry.Figure.Cogs,
                entry.Profit,
                CanonicalFigures.MarginRate(entry.Figure.Net, entry.Profit),
                CanonicalFigures.Share(entry.Figure.Net, totalNet),
                Period: null)),
        ];
    }

    private static ProfitRow PeriodRow(string name, Money net, Money cogs, Money totalNet, ReportDateRange period)
    {
        var profit = CanonicalFigures.GrossProfit(net, cogs);

        return new ProfitRow(
            Rank: null,
            Key: null,
            Code: string.Empty,
            Name: name,
            QtyBase: Quantity.Zero(0),
            UomSymbol: null,
            Net: net,
            Cogs: cogs,
            GrossProfit: profit,
            MarginRate: CanonicalFigures.MarginRate(net, profit),
            ShareOfNet: CanonicalFigures.Share(net, totalNet),
            Period: period);
    }
}
