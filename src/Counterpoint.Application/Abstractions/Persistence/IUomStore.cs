using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>uom</c> (docs/01_DATA_MODEL.md §3).
/// </summary>
public interface IUomStore
{
    public Task<IReadOnlyList<UomRecord>> ListAsync(CancellationToken cancellationToken = default);

    public Task<UomRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    public Task<bool> ExistsWithNameAsync(string name, long? excludingId, CancellationToken cancellationToken = default);

    /// <summary>True when a product's base unit, or one of its alternate units, is this one.</summary>
    public Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken = default);

    public Task<long> CreateAsync(
        string name,
        string symbol,
        int decimalPlaces,
        CancellationToken cancellationToken = default);

    public Task UpdateAsync(
        long id,
        string name,
        string symbol,
        int decimalPlaces,
        CancellationToken cancellationToken = default);

    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database's foreign keys refused the delete.</summary>
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
