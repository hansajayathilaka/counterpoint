using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Dashboard;

/// <summary>
/// The home screen's compact dashboard (SRS FR-9.7): today's sales, bill count, average bill,
/// cash in drawer, low-stock count and last backup status.
/// </summary>
/// <remarks>
/// No <c>RequiresRoleAttribute</c>: none of the six figures is cost, margin or profit - the
/// figures FR-9.4 reserves for the owner role - so this is available to a cashier session exactly
/// as <c>IStockEnquiry</c> is (CLAUDE.md invariant 8).
/// </remarks>
public interface IDashboardQueries
{
    /// <summary>Today's figures, recomputed from the database on every call.</summary>
    public Task<DashboardSummary> GetSummaryAsync(CancellationToken cancellationToken = default);
}
