using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>Reads and writes <c>customer</c> (SRS FR-6.1).</summary>
public interface ICustomerStore
{
    public Task<IReadOnlyList<CustomerRecord>> ListAsync(CancellationToken cancellationToken = default);

    public Task<CustomerRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when at least one <c>sale</c> names this customer. <c>sale.customer_id</c> carries no
    /// foreign key yet (that arrives with credit accounts in P5-T02), so this is a plain count
    /// rather than something the database would refuse on its own.
    /// </summary>
    public Task<bool> HasSalesAsync(long id, CancellationToken cancellationToken = default);

    public Task<long> CreateAsync(NewCustomer customer, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, NewCustomer customer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns a customer on or off. Always permitted - unlike a category, brand, unit or tax class,
    /// a customer carries no foreign key from <c>product</c>, so there is no in-use state that
    /// would make deactivating one unsafe.
    /// </summary>
    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default);

    /// <summary>Returns false when a sale references this customer.</summary>
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
