using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>Reads and writes <c>brand</c> (SRS FR-2.21).</summary>
public interface IBrandStore
{
    public Task<IReadOnlyList<BrandRecord>> ListAsync(CancellationToken cancellationToken = default);

    public Task<BrandRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    public Task<bool> ExistsWithNameAsync(string name, long? excludingId, CancellationToken cancellationToken = default);

    /// <summary>True when at least one product carries this brand.</summary>
    public Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken = default);

    public Task<long> CreateAsync(string name, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, string name, CancellationToken cancellationToken = default);

    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database's foreign keys refused the delete.</summary>
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
