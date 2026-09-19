using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// The cost-bearing companion to <see cref="ISalesPeriodSummaryQuery"/>: the same canonical period
/// figures plus COGS and gross profit, owner-only (task P3-T04 "Do this" #2 and #4, SRS FR-9.4,
/// RPT-03/RPT-07, CLAUDE.md invariant 8).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only, enforced in the Application layer.</b> <see cref="RequiresRoleAttribute"/> on the
/// interface means the concrete reader registered in <c>Counterpoint.Reporting</c> is wrapped with
/// <c>RoleAuthorisation</c>, so a cashier session cannot reach a single one of these figures - not
/// by asking for the interface, not by resolving the concrete query (which is <c>internal</c> and
/// is never registered bare). The cost-bearing projection is kept off the cashier's DTO entirely
/// (SRS FR-9.4, AC-17).
/// </para>
/// <para>
/// <b>COGS is the snapshot cost, never the current one.</b> It is <c>sale.cogs</c> - the cost
/// captured on the sale header at the time of sale - minus the returned equivalent priced from
/// <c>sale_return_line.unit_cost</c>. A later change to a product's cost must not move a past
/// period's profit (task P3-T05's own risk note; CLAUDE.md invariant 10).
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IProfitPeriodSummaryQuery
{
    /// <summary>The canonical period figures plus COGS, gross profit and margin for <paramref name="range"/>.</summary>
    /// <param name="range">The inclusive business-date range to summarise.</param>
    /// <param name="sourcePolicy">
    /// Whether the query may read rollups for closed dates (the default) or must read raw tables for
    /// the whole range (<see cref="ReportSourcePolicy"/>).
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<ProfitPeriodSummary> GetProfitSummaryAsync(
        ReportDateRange range,
        ReportSourcePolicy sourcePolicy = ReportSourcePolicy.RollupsWhereClosed,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The canonical period figures plus the cost-derived ones, for an owner-only report (task
/// P3-T04 "Do this" #4).
/// </summary>
/// <param name="Range">The range these figures cover.</param>
/// <param name="BillCount">Completed bills in the range.</param>
/// <param name="ReturnCount">Returns taken in the range.</param>
/// <param name="GrossSales">Sales before any discount, excluding tax.</param>
/// <param name="Discounts">Line-level plus bill-level discounts.</param>
/// <param name="Tax">Tax charged on the range's completed sales.</param>
/// <param name="NetSales">Gross sales minus discounts minus returns, excluding tax.</param>
/// <param name="ReturnsValue">The total refunded by the range's returns.</param>
/// <param name="TenderTotal">The sum of <c>payment.amount</c> for the range.</param>
/// <param name="Cogs">Cost of goods sold at the snapshot cost, net of returns.</param>
/// <param name="GrossProfit"><see cref="NetSales"/> minus <see cref="Cogs"/>.</param>
/// <param name="MarginRate">
/// <see cref="GrossProfit"/> divided by <see cref="NetSales"/>, as a fraction (0.25 = 25%), or zero
/// when net sales is zero. A <see cref="decimal"/> - money and rates are never binary floating point
/// (CLAUDE.md invariant 1).
/// </param>
public sealed record ProfitPeriodSummary(
    ReportDateRange Range,
    int BillCount,
    int ReturnCount,
    Money GrossSales,
    Money Discounts,
    Money Tax,
    Money NetSales,
    Money ReturnsValue,
    Money TenderTotal,
    Money Cogs,
    Money GrossProfit,
    decimal MarginRate);
