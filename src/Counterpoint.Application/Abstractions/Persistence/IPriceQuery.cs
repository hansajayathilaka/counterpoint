using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Finds the variants a bulk price update (SRS FR-2.19) would touch.
/// </summary>
public interface IPriceQuery
{
    /// <summary>Every active variant matching <paramref name="filter"/>, with its current price and product cost.</summary>
    public Task<IReadOnlyList<PriceQueryVariant>> FindVariantsAsync(
        BulkPriceQueryFilter filter,
        CancellationToken cancellationToken = default);
}
