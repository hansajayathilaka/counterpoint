using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Replays the whole ledger into the balance projection (CLAUDE.md invariant 3, P1-T07).
/// </summary>
/// <remarks>
/// The projection is a cache, rebuildable from the ledger by definition. This is the maintenance
/// operation that proves it: a repair after a suspected inconsistency, or the seam a startup
/// consistency check's mismatch would call for.
/// </remarks>
public interface IRebuildStockBalance
{
    /// <summary>
    /// Recomputes every row of the balance projection from <c>stock_movement</c> alone, replacing
    /// whatever is there now.
    /// </summary>
    /// <param name="cancellationToken">Cancels the rebuild.</param>
    public Task<StockBalanceRebuildSummary> RebuildAsync(CancellationToken cancellationToken = default);
}

/// <summary>What a rebuild did.</summary>
/// <param name="VariantCount">How many variants' balances were recomputed.</param>
/// <param name="MovementCount">How many ledger rows were replayed to get there.</param>
public sealed record StockBalanceRebuildSummary(int VariantCount, long MovementCount);
