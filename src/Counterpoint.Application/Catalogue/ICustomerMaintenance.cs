using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>Maintaining <c>customer</c> (SRS FR-6.1). Owner only, matching every other reference-data
/// screen in this task (AC-17); a walk-in sale itself never needs one.</summary>
[RequiresRole(Role.Owner)]
public interface ICustomerMaintenance
{
    public Task<IReadOnlyList<CustomerRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException"><c>Type</c> is neither RETAIL nor TRADE.</exception>
    public Task<long> CreateAsync(SaveCustomerCommand command, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, SaveCustomerCommand command, CancellationToken cancellationToken = default);

    /// <summary>Turns a customer off. Always succeeds - a customer carries no product-side reference.</summary>
    public Task DeactivateAsync(long id, CancellationToken cancellationToken = default);

    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">A sale is recorded against this customer.</exception>
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
