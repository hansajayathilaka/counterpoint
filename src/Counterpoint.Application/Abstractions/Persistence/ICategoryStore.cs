using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>category</c> (SRS FR-2.20, FR-2.21).
/// </summary>
/// <remarks>
/// A port: the Application layer says what it needs, <c>Counterpoint.Infrastructure</c> supplies
/// the SQLite implementation. The two-level rule is enforced twice - once here, ahead of the
/// write, so the common case never touches the trigger's raw message; and once by
/// <c>trg_category_two_levels_*</c> as the backstop the caller cannot go round.
/// </remarks>
public interface ICategoryStore
{
    /// <summary>Every category, active and not, parent name resolved for display.</summary>
    public Task<IReadOnlyList<CategoryRecord>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>The category with this id, or null.</summary>
    public Task<CategoryRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>True when a category with this name already exists at this level.</summary>
    public Task<bool> ExistsWithNameAsync(
        string name,
        long? parentId,
        long? excludingId,
        CancellationToken cancellationToken = default);

    /// <summary>True when at least one category has <paramref name="id"/> as its parent.</summary>
    public Task<bool> HasChildrenAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>True when at least one product is classified under this category.</summary>
    public Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Inserts a category and returns the new id. Joins the caller's transaction.</summary>
    public Task<long> CreateAsync(string name, long? parentId, CancellationToken cancellationToken = default);

    /// <summary>Renames a category and/or moves it under a different parent. Joins the caller's transaction.</summary>
    public Task UpdateAsync(long id, string name, long? parentId, CancellationToken cancellationToken = default);

    /// <summary>Turns a category on or off. Joins the caller's transaction.</summary>
    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a category outright. Returns false, rather than throwing, when the database's own
    /// foreign keys refused it - the backstop for a reference this store's pre-checks missed.
    /// Joins the caller's transaction.
    /// </summary>
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
}
