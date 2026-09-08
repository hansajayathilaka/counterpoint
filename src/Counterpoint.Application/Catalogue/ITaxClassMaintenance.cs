using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>Maintaining <c>tax_class</c> after first run (SRS FR-10.3, Q-02). Owner only (AC-17).</summary>
[RequiresRole(Role.Owner)]
public interface ITaxClassMaintenance
{
    public Task<IReadOnlyList<TaxClassRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">The name is already used by another tax class.</exception>
    public Task<long> CreateAsync(SaveTaxClassCommand command, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, SaveTaxClassCommand command, CancellationToken cancellationToken = default);

    /// <summary>Turns a tax class off. Always succeeds - the FR-2.1 pattern, applied to tax classes.</summary>
    public Task DeactivateAsync(long id, CancellationToken cancellationToken = default);

    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">A product is classified under this tax class.</exception>
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
