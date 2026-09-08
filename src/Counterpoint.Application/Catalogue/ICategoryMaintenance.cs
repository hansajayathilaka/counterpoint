using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// Maintaining <c>category</c>: two levels, enforced (SRS FR-2.20). Owner only, the same as every
/// other reference-data screen (SRS §3.3 ROLE-2, NFR-S2, AC-17) - this is product management, not
/// a cashier action.
/// </summary>
[RequiresRole(Role.Owner)]
public interface ICategoryMaintenance
{
    public Task<IReadOnlyList<CategoryRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">
    /// The name is already used at this level, the parent does not exist, or the parent is
    /// already a sub-category - a category can only be two levels deep (FR-2.20).
    /// </exception>
    public Task<long> CreateAsync(SaveCategoryCommand command, CancellationToken cancellationToken = default);

    /// <exception cref="System.InvalidOperationException">
    /// As <see cref="CreateAsync"/>, or the move would give this category both children and a
    /// parent at once.
    /// </exception>
    public Task UpdateAsync(long id, SaveCategoryCommand command, CancellationToken cancellationToken = default);

    /// <summary>Turns a category off. Always succeeds - the FR-2.1 pattern, applied to categories.</summary>
    public Task DeactivateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Turns a deactivated category back on.</summary>
    public Task ReactivateAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a category outright. Only a category with no products and no sub-categories can be
    /// removed this way; one that is in use must be deactivated instead.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// A product is classified under this category, or it has sub-categories.
    /// </exception>
    public Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
