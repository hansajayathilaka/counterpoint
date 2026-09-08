using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>tax_class</c> for the reference-data screen (SRS FR-10.3, Q-02).
/// </summary>
/// <remarks>
/// Separate from <see cref="ITaxClassSeed"/> on purpose: that port only ever creates a class that
/// is missing, for the first-run wizard; this one is the owner's ongoing maintenance - renaming,
/// re-rating, deactivating - and the two must not be confused into one seam that would let a
/// screen quietly "seed" a duplicate class under a slightly different name.
/// </remarks>
public interface ITaxClassStore
{
    public Task<IReadOnlyList<TaxClassRecord>> ListAsync(CancellationToken cancellationToken = default);

    public Task<TaxClassRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    public Task<bool> ExistsWithNameAsync(string name, long? excludingId, CancellationToken cancellationToken = default);

    /// <summary>True when at least one product is classified under this tax class.</summary>
    public Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken = default);

    public Task<long> CreateAsync(string name, TaxRate rate, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, string name, TaxRate rate, CancellationToken cancellationToken = default);

    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database's foreign keys refused the delete.</summary>
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
