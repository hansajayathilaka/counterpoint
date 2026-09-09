using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads a completed sale back, for cancellation (SRS FR-3.34).
/// </summary>
/// <remarks>
/// A read connection, not the write one, and read before the cancellation's own transaction opens
/// - the same NFR-P3 discipline <c>CompleteSaleHandler</c> keeps for pricing: the writer lock is
/// for the write alone. <c>sale</c> and <c>stock_movement</c> are both append-only, so nothing
/// this reads can go stale between the read and the write it informs.
/// </remarks>
public interface ISaleLookup
{
    /// <summary>The sale and its original stock movements, or null when no such sale exists.</summary>
    public Task<SaleForCancellation?> FindForCancellationAsync(
        long saleId,
        CancellationToken cancellationToken = default);
}
