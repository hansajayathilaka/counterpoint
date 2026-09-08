using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>Maintaining <c>brand</c> (SRS FR-2.21). Owner only, as product management is (AC-17).</summary>
[RequiresRole(Role.Owner)]
public interface IBrandMaintenance
{
    public Task<IReadOnlyList<BrandRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">The name is already used by another brand.</exception>
    public Task<long> CreateAsync(SaveBrandCommand command, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, SaveBrandCommand command, CancellationToken cancellationToken = default);

    /// <summary>Turns a brand off. Always succeeds - the FR-2.1 pattern, applied to brands.</summary>
    public Task DeactivateAsync(long id, CancellationToken cancellationToken = default);

    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">A product carries this brand.</exception>
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
