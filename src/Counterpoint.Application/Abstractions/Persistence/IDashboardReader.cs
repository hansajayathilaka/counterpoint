using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The three read-only aggregates behind the compact home-screen dashboard (SRS FR-9.7) that are
/// not already answered by an existing port: today's sales, cash in the drawer, and how many
/// items are low on stock.
/// </summary>
/// <remarks>
/// <para>
/// Every query is scoped so it cannot become a whole-table scan as the shop's history grows
/// (P1-T14 "Risks"): today's sales reads <c>sale</c> through its <c>business_date</c> index,
/// exactly the column every other rollup in this system groups by; cash in drawer reads one
/// <c>shift</c> row and the handful of payments posted against it since it opened; low stock
/// counts active products, bounded by the catalogue's own size, not by how many bills have ever
/// been rung up.
/// </para>
/// <para>
/// Hand-written SQL over a read connection, not EF - the same split <c>IStockPositionReader</c>
/// and <c>IProductLookup</c> draw (CLAUDE.md "Stack"): this is a read path the dashboard polls on
/// a timer, and it must never compete with the single write connection a sale is using.
/// </para>
/// </remarks>
public interface IDashboardReader
{
    /// <summary>Bill count and total for one business day (SRS FR-9.7's "today's sales" and "bill count").</summary>
    public Task<DashboardSalesSummary> GetTodaysSalesAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The current open shift's opening float plus its cash sales so far, or null when no shift
    /// is open. Phase 1 has no cash-in/cash-out movements yet (those are P3-T01), so this is the
    /// whole of "cash in drawer" until then.
    /// </summary>
    public Task<Money?> GetCashInDrawerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many active products are at or below their reorder level (SRS FR-2.2, FR-4.17), summed
    /// across their variants. A product with no reorder level configured (the default, zero) is
    /// never counted - zero means "not tracked", not "reorder immediately".
    /// </summary>
    public Task<int> GetLowStockCountAsync(CancellationToken cancellationToken = default);
}
