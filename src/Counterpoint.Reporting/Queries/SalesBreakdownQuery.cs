using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="ISalesBreakdownQuery"/>: RPT-02, ranked by net sales (task P3-T05 "Do this" #2).
/// </summary>
/// <remarks>
/// Cost-free: <see cref="ItemFiguresReader"/> is asked for <c>includeCost: false</c>, so no cost
/// column is read, and <see cref="SalesBreakdownRow"/> has no field to carry one. Ranking is net
/// descending, then name, then key - a total order, so two runs over the same data rank identically.
/// </remarks>
internal sealed class SalesBreakdownQuery : ISalesBreakdownQuery
{
    private readonly ItemFiguresReader _items;

    public SalesBreakdownQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _items = new ItemFiguresReader(connectionFactory);
    }

    /// <inheritdoc />
    public async Task<SalesBreakdownReport> GetBreakdownAsync(
        ReportDateRange range,
        SalesBreakdownDimension dimension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var figures = await _items.ReadAsync(range, dimension, includeCost: false, cancellationToken)
            .ConfigureAwait(false);

        var ordered = figures
            .OrderByDescending(figure => figure.Net.Amount)
            .ThenBy(figure => figure.Name, StringComparer.Ordinal)
            .ThenBy(figure => figure.Key)
            .ToList();

        var totalNet = CanonicalFigures.Sum(ordered.Select(figure => figure.Net));

        return new SalesBreakdownReport(
            range,
            dimension,
            totalNet,
            [
                .. ordered.Select((figure, index) => new SalesBreakdownRow(
                    index + 1,
                    figure.Key,
                    figure.Code,
                    figure.Name,
                    figure.QtyBase,
                    figure.UomSymbol,
                    figure.Net,
                    CanonicalFigures.Share(figure.Net, totalNet))),
            ]);
    }
}
