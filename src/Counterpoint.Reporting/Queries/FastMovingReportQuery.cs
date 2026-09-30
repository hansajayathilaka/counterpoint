using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="IFastMovingReportQuery"/>: top sellers by units and by value (task P3-T06 "Do this" #7, SRS RPT-13).
/// </summary>
/// <remarks>
/// A thin projection: it ranks nothing the sales-by-item report does not already hold. It asks
/// <see cref="ISalesBreakdownQuery"/> for the item dimension and re-sorts and truncates its rows - by net for
/// "by value" (the order they already arrive in) and by net units for "by units" - so the figures are that
/// report's, net of returns and excluding tax, with no cost field read. The open-item row (no product) is not
/// an item and is left out; a row with no net units sold (or no net value) cannot be a fast mover and is left
/// out of that ranking. Owner-only, registered only wrapped with <c>RoleAuthorisation</c>.
/// </remarks>
internal sealed class FastMovingReportQuery : IFastMovingReportQuery
{
    private readonly ISalesBreakdownQuery _breakdown;

    public FastMovingReportQuery(ISalesBreakdownQuery breakdown)
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        _breakdown = breakdown;
    }

    /// <inheritdoc />
    public async Task<FastMovingReport> GetReportAsync(
        ReportDateRange range,
        int topN = 20,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);
        ArgumentOutOfRangeException.ThrowIfLessThan(topN, 1);

        var breakdown = await _breakdown.GetBreakdownAsync(range, SalesBreakdownDimension.Item, cancellationToken)
            .ConfigureAwait(false);

        var items = breakdown.Rows.Where(row => row.Key is not null).ToList();

        var byValue = items
            .Where(row => row.Net.Amount > 0m)
            .OrderByDescending(row => row.Net)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .Take(topN)
            .Select((row, index) => ToRow(row, index + 1))
            .ToList();

        var byUnits = items
            .Where(row => row.QtyBase.Value > 0m)
            .OrderByDescending(row => row.QtyBase.Value)
            .ThenByDescending(row => row.Net)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .Take(topN)
            .Select((row, index) => ToRow(row, index + 1))
            .ToList();

        return new FastMovingReport(range, topN, byUnits, byValue);
    }

    private static FastMovingRow ToRow(SalesBreakdownRow row, int rank) => new(
        rank,
        row.Key,
        row.Code,
        row.Name,
        row.QtyBase,
        row.UomSymbol,
        row.Net);
}
