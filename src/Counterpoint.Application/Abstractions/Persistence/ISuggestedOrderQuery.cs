using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The suggested-order report: every active product at or below its reorder level, with the
/// shop's own reorder quantity proposed (SRS FR-4.6, task P2-T06 "Do this" #3).
/// </summary>
/// <remarks>
/// A read port over hand-written SQL, the same split <see cref="IDashboardReader"/> draws
/// (CLAUDE.md "Stack"): this never competes with the single write connection a sale or a goods
/// receipt is using, and it never touches the stock ledger or its projection - it only reads what
/// <c>Counterpoint.Application.Inventory</c>'s ledger has already written (CLAUDE.md invariant 3).
/// </remarks>
public interface ISuggestedOrderQuery
{
    /// <summary>
    /// Every active product whose summed stock across its active variants is at or below its own
    /// reorder level, ordered by how far under it sits. A product whose reorder level is still the
    /// default zero is never proposed - zero means "not tracked", not "reorder immediately",
    /// exactly the reasoning <see cref="IDashboardReader.GetLowStockCountAsync"/> already applies.
    /// </summary>
    public Task<IReadOnlyList<SuggestedOrderLine>> GetSuggestedOrderAsync(CancellationToken cancellationToken = default);
}
