using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>Reads and writes <c>supplier</c> (SRS FR-6.5).</summary>
public interface ISupplierStore
{
    public Task<IReadOnlyList<SupplierRecord>> ListAsync(CancellationToken cancellationToken = default);

    public Task<SupplierRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when this supplier is linked to a product (<c>product_supplier</c>), or named on a
    /// purchase order or goods receipt - the Phase 2 tables already exist even though nothing
    /// writes to them yet, and a delete guard that ignored them would be a promise this store
    /// cannot keep once P2-T06/P2-T07 land.
    /// </summary>
    public Task<bool> HasLinksAsync(long id, CancellationToken cancellationToken = default);

    public Task<long> CreateAsync(NewSupplier supplier, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, NewSupplier supplier, CancellationToken cancellationToken = default);

    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default);

    /// <summary>Returns false when the database's foreign keys refused the delete.</summary>
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
