using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// The canonical period figures every sales-side report is built on - gross sales, discounts, tax,
/// net sales, returns and the tender total for a business-date range (task P3-T04 "Do this" #2,
/// SRS FR-9.1, FR-9.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>One definition, one implementation.</b> Each figure here is defined in
/// <c>docs/report-definitions.md</c> and implemented exactly once, in
/// <c>Counterpoint.Reporting.Queries.PeriodFiguresReader</c>. Every report that shows "net sales"
/// asks this query rather than writing its own <c>SUM(...)</c>, which is what makes SRS AC-12
/// ("report totals reconcile") achievable instead of a recurring bug (task P3-T04's own context).
/// </para>
/// <para>
/// <b>Not owner-only.</b> None of the figures on <see cref="SalesPeriodSummary"/> is cost or margin
/// (CLAUDE.md invariant 8): the DTO has no cost field at all, so a cashier session can run it for
/// RPT-01/RPT-02 without ever being handed a cost figure. The cost-bearing companion,
/// <see cref="IProfitPeriodSummaryQuery"/>, is owner-only and carries the same figures plus COGS
/// and gross profit.
/// </para>
/// <para>
/// <b>Reads rollups where it can, raw tables where it must.</b> See
/// <see cref="ReportSourcePolicy"/> - the default reads a trustworthy rollup for a closed date and
/// the raw tables for the open shift's own date, for any closed date with no rollup row, and for any
/// date whose rollup a later cancellation has made stale; both settings return the same figures for
/// the same range.
/// </para>
/// </remarks>
public interface ISalesPeriodSummaryQuery
{
    /// <summary>The canonical period figures for <paramref name="range"/>.</summary>
    /// <param name="range">The inclusive business-date range to summarise.</param>
    /// <param name="sourcePolicy">
    /// Whether the query may read rollups for closed dates (the default) or must read raw tables for
    /// the whole range. Both return the same figures; the choice is a performance and dimensions
    /// one, not a correctness one (<see cref="ReportSourcePolicy"/>).
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<SalesPeriodSummary> GetSalesSummaryAsync(
        ReportDateRange range,
        ReportSourcePolicy sourcePolicy = ReportSourcePolicy.RollupsWhereClosed,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The canonical period figures for a range, without any cost or margin figure - the projection a
/// cashier session is allowed to see (task P3-T04 "Do this" #4, CLAUDE.md invariant 8, SRS AC-17).
/// </summary>
/// <param name="Range">The range these figures cover.</param>
/// <param name="BillCount">Completed bills in the range (<c>sale.status = 'COMPLETED'</c>).</param>
/// <param name="ReturnCount">Returns taken in the range.</param>
/// <param name="GrossSales">Sales before any discount, excluding tax - <c>subtotal + line_discount</c>.</param>
/// <param name="Discounts">Line-level plus bill-level discounts.</param>
/// <param name="Tax">Tax charged on the range's completed sales.</param>
/// <param name="NetSales">Gross sales minus discounts minus returns, excluding tax.</param>
/// <param name="ReturnsValue">The total refunded by the range's returns (<c>sale_return.total_refund</c>).</param>
/// <param name="TenderTotal">The sum of <c>payment.amount</c> for the range, sales less refunds.</param>
public sealed record SalesPeriodSummary(
    ReportDateRange Range,
    int BillCount,
    int ReturnCount,
    Money GrossSales,
    Money Discounts,
    Money Tax,
    Money NetSales,
    Money ReturnsValue,
    Money TenderTotal);
