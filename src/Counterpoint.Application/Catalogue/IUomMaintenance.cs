using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// Maintaining <c>uom</c>. Owner only, as product management is (AC-17).
/// </summary>
/// <remarks>
/// No deactivate or reactivate: see the remarks on <see cref="UomRecord"/>. A unit already in use
/// can be renamed but not hidden from new use; deleting it is refused while any product
/// references it.
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IUomMaintenance
{
    public Task<IReadOnlyList<UomRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">
    /// The name is already used, or <c>DecimalPlaces</c> is outside 0-4 (<c>ck_uom_decimal_places</c>).
    /// </exception>
    public Task<long> CreateAsync(SaveUomCommand command, CancellationToken cancellationToken = default);

    public Task UpdateAsync(long id, SaveUomCommand command, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">A product references this unit.</exception>
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
