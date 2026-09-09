using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>price_change_log</c> (docs/01_DATA_MODEL.md §3, SRS FR-2.17).
/// </summary>
/// <remarks>
/// Append-only, the same way <see cref="IAuditTrail"/> is: there is no update or delete, because
/// a price-change history that could be edited would not be history.
/// </remarks>
public interface IPriceChangeLogStore
{
    /// <summary>Appends one row, in the caller's transaction.</summary>
    public Task RecordAsync(NewPriceChangeLogEntry entry, CancellationToken cancellationToken = default);

    /// <summary>One variant's full price history, most recent first.</summary>
    public Task<IReadOnlyList<PriceChangeLogEntry>> ListByVariantAsync(
        long productVariantId,
        CancellationToken cancellationToken = default);
}
