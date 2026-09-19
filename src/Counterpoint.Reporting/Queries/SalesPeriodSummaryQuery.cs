using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="ISalesPeriodSummaryQuery"/>: projects the canonical
/// <see cref="PeriodTotals"/> onto the cashier-safe DTO, dropping every cost-derived field entirely
/// (task P3-T04 "Do this" #4, CLAUDE.md invariant 8).
/// </summary>
/// <remarks>
/// <b>Not owner-only, and deliberately cost-free.</b> It never calls
/// <see cref="PeriodFiguresReader.ReadWithCogsAsync"/> - it reads only
/// <see cref="PeriodFiguresReader.ReadTotalsAsync"/>, so a cashier session neither receives nor
/// computes a cost figure. The cost-bearing companion is
/// <see cref="ProfitPeriodSummaryQuery"/>, which is decorated with the owner role check in
/// <c>ReportingServiceCollectionExtensions</c>.
/// <para>
/// <b>The shared engine is built here, never injected.</b> <see cref="PeriodFiguresReader"/> is not
/// registered in the container, so neither this query nor a future P3-T05/P3-T06 query can acquire an
/// undeclared, COGS-bearing instance by constructor injection.
/// </para>
/// </remarks>
internal sealed class SalesPeriodSummaryQuery : ISalesPeriodSummaryQuery
{
    private readonly PeriodFiguresReader _reader;

    public SalesPeriodSummaryQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _reader = new PeriodFiguresReader(connectionFactory);
    }

    /// <inheritdoc />
    public async Task<SalesPeriodSummary> GetSalesSummaryAsync(
        ReportDateRange range,
        ReportSourcePolicy sourcePolicy = ReportSourcePolicy.RollupsWhereClosed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var totals = await _reader.ReadTotalsAsync(range, sourcePolicy, cancellationToken).ConfigureAwait(false);

        return new SalesPeriodSummary(
            range,
            totals.BillCount,
            totals.ReturnCount,
            totals.GrossSales,
            totals.Discounts,
            totals.Tax,
            totals.NetSales,
            totals.ReturnsValue,
            totals.TenderTotal);
    }
}
