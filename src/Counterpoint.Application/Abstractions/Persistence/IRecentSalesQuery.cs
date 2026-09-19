using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The dashboard's recent-sales list (SRS FR-9.7, task P3-T22): the last N completed bills, most
/// recent first.
/// </summary>
/// <remarks>
/// <para>
/// No existing port answers "the last N completed sales" - <c>IDashboardQueries</c> returns
/// aggregate figures only, and <see cref="IReturnableSaleLookup"/>'s
/// <see cref="IReturnableSaleLookup.SearchAsync"/> searches by date/customer/amount criteria for
/// the returns flow, not a fixed-length recency-ordered list for a landing screen.
/// </para>
/// <para>
/// No <c>RequiresRoleAttribute</c>: bill number, completed time, customer name and total are the
/// same cashier-visible figures <c>IDashboardQueries</c> and <see cref="IReturnableSaleLookup"/>
/// already hand out undecorated - no cost or margin, the two figures CLAUDE.md invariant 8
/// reserves for the owner role.
/// </para>
/// </remarks>
public interface IRecentSalesQuery
{
    /// <summary>
    /// The most recent <paramref name="count"/> completed sales, most recent first. A cancelled
    /// sale is never included.
    /// </summary>
    public Task<IReadOnlyList<RecentSale>> GetRecentAsync(int count, CancellationToken cancellationToken = default);
}
