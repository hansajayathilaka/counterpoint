using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>Maintaining <c>supplier</c> (SRS FR-6.5). Owner only (AC-17).</summary>
[RequiresRole(Role.Owner)]
public interface ISupplierMaintenance
{
    public Task<IReadOnlyList<SupplierRecord>> ListAsync(CancellationToken cancellationToken = default);

    public Task<long> CreateAsync(SaveSupplierCommand command, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, SaveSupplierCommand command, CancellationToken cancellationToken = default);

    /// <summary>Turns a supplier off. Always succeeds - the FR-2.1 pattern, applied to suppliers.</summary>
    public Task DeactivateAsync(long id, CancellationToken cancellationToken = default);

    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">
    /// A product, purchase order or goods receipt is linked to this supplier.
    /// </exception>
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
