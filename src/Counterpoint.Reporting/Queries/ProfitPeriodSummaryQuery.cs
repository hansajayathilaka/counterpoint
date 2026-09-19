using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="IProfitPeriodSummaryQuery"/>: the canonical period figures plus COGS and gross profit
/// (task P3-T04 "Do this" #2 and #4).
/// </summary>
/// <remarks>
/// <b>Owner-only by construction.</b> The interface carries <c>[RequiresRole(Role.Owner)]</c> and
/// this class is <c>internal</c>, so the only registration that exists is the one
/// <c>ReportingServiceCollectionExtensions</c> builds by wrapping this instance with
/// <c>RoleAuthorisation</c> - nothing can resolve an undecorated profit query from the container
/// (the same discipline <c>StockValuationQuery</c> already holds, SRS AC-17).
/// <para>
/// <b>The shared engine is built here, never injected.</b> <see cref="PeriodFiguresReader"/> is not
/// registered in the container: the only way to get the COGS-bearing reader is to build a profit
/// query, and the only profit query the container hands out is the role-decorated one.
/// </para>
/// </remarks>
internal sealed class ProfitPeriodSummaryQuery : IProfitPeriodSummaryQuery
{
    private readonly PeriodFiguresReader _reader;

    public ProfitPeriodSummaryQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _reader = new PeriodFiguresReader(connectionFactory);
    }

    /// <inheritdoc />
    public async Task<ProfitPeriodSummary> GetProfitSummaryAsync(
        ReportDateRange range,
        ReportSourcePolicy sourcePolicy = ReportSourcePolicy.RollupsWhereClosed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var (totals, cogs) = await _reader.ReadWithCogsAsync(range, sourcePolicy, cancellationToken)
            .ConfigureAwait(false);

        // The canonical gross-profit definition, implemented here and nowhere else: net sales minus
        // COGS. The margin rate is that figure over net sales, guarded against a zero denominator.
        var grossProfit = totals.NetSales - cogs;
        var marginRate = totals.NetSales.Amount == 0m
            ? 0m
            : grossProfit.Amount / totals.NetSales.Amount;

        return new ProfitPeriodSummary(
            range,
            totals.BillCount,
            totals.ReturnCount,
            totals.GrossSales,
            totals.Discounts,
            totals.Tax,
            totals.NetSales,
            totals.ReturnsValue,
            totals.TenderTotal,
            cogs,
            grossProfit,
            marginRate);
    }
}
