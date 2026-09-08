using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// The owner's category maintenance (SRS FR-2.20, FR-2.21).
/// </summary>
/// <remarks>
/// <para>
/// The two-level rule is checked here, ahead of the write, against the parent's own
/// <c>parent_id</c> and against whether this category already has children of its own - so the
/// common case gets a sentence about categories, never <c>trg_category_two_levels_*</c>'s raw
/// SQLite message. The trigger stays the backstop for a race this pre-check cannot see (two
/// screens editing the same tree at once is not this till's shape, but the trigger does not know
/// that either, and is cheap insurance regardless).
/// </para>
/// <para>
/// Internal, for the same reason <c>UserAdministrationService</c> is: the role check on
/// <see cref="ICategoryMaintenance"/> only holds if nothing outside this assembly can construct
/// the class the check is supposed to be in front of.
/// </para>
/// </remarks>
internal sealed class CategoryMaintenanceService : ICategoryMaintenance
{
    private readonly ICategoryStore _store;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public CategoryMaintenanceService(
        ICategoryStore store,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CategoryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateAsync(SaveCategoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = RequireName(command.Name);
        var parentId = command.ParentId;

        if (parentId is { } candidateParent)
        {
            await RequireValidParentAsync(candidateParent, cancellationToken).ConfigureAwait(false);
        }

        if (await _store.ExistsWithNameAsync(name, parentId, excludingId: null, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a category called '{name}' at this level. Pick another name."));
        }

        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _store.CreateAsync(name, parentId, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Created,
                    id,
                    now,
                    before: null,
                    after: Json(name, parentId),
                    token).ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(long id, SaveCategoryCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await RequireCategoryAsync(id, cancellationToken).ConfigureAwait(false);
        var name = RequireName(command.Name);
        var parentId = command.ParentId;

        if (parentId == id)
        {
            throw new InvalidOperationException("A category cannot be its own parent.");
        }

        if (parentId is { } candidateParent)
        {
            await RequireValidParentAsync(candidateParent, cancellationToken).ConfigureAwait(false);

            if (await _store.HasChildrenAsync(id, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.CurrentCulture,
                    $"'{existing.Name}' already has sub-categories, so it cannot become a sub-category itself."));
            }
        }

        if (await _store.ExistsWithNameAsync(name, parentId, excludingId: id, cancellationToken)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a category called '{name}' at this level. Pick another name."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateAsync(id, name, parentId, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    id,
                    now,
                    before: Json(existing.Name, existing.ParentId),
                    after: Json(name, parentId),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireCategoryAsync(id, cancellationToken).ConfigureAwait(false);

        if (!existing.Active)
        {
            return;
        }

        await SetActiveAsync(id, existing.Name, active: false, CatalogueAuditActions.Deactivated, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireCategoryAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing.Active)
        {
            return;
        }

        await SetActiveAsync(id, existing.Name, active: true, CatalogueAuditActions.Reactivated, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireCategoryAsync(id, cancellationToken).ConfigureAwait(false);

        if (await _store.HasProductsAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"'{existing.Name}' has products classified under it and cannot be deleted. Deactivate it instead."));
        }

        if (await _store.HasChildrenAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"'{existing.Name}' has sub-categories and cannot be deleted. Deactivate it instead."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (!await _store.DeleteAsync(id, token).ConfigureAwait(false))
                {
                    // Backstop: something references this row that the checks above did not see.
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.CurrentCulture,
                        $"'{existing.Name}' is referenced elsewhere and cannot be deleted. Deactivate it instead."));
                }

                await RecordAsync(
                    CatalogueAuditActions.Deleted,
                    id,
                    now,
                    before: Json(existing.Name, existing.ParentId),
                    after: null,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RequireValidParentAsync(long parentId, CancellationToken cancellationToken)
    {
        var parent = await _store.FindByIdAsync(parentId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is no category with id {parentId} to use as a parent."));

        if (parent.ParentId is not null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"A category can only be two levels deep. '{parent.Name}' is already a sub-category, so it cannot have sub-categories of its own."));
        }
    }

    private async Task SetActiveAsync(
        long id,
        string name,
        bool active,
        string action,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.SetActiveAsync(id, active, token).ConfigureAwait(false);

                await RecordAsync(
                    action,
                    id,
                    now,
                    before: SecurityAuditJson.Object(("name", name), ("active", !active)),
                    after: SecurityAuditJson.Object(("name", name), ("active", active)),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CategoryRecord> RequireCategoryAsync(long id, CancellationToken cancellationToken) =>
        await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.CurrentCulture,
            $"There is no category with id {id}. It may have been removed since this screen was opened."));

    private static string RequireName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A category needs a name.");
        }

        return trimmed;
    }

    private static string Json(string name, long? parentId) =>
        SecurityAuditJson.Object(
            ("name", name),
            ("parent_id", parentId?.ToString(CultureInfo.InvariantCulture)));

    private Task RecordAsync(
        string action,
        long categoryId,
        DateTimeOffset now,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Category maintenance ran without a session. The role decorator should have refused "
            + "this call; the service is registered without it.");

        return _audit.RecordAsync(
            new AuditEntry(
                now,
                actor.Id,
                action,
                CatalogueAuditActions.CategoryEntityType,
                categoryId,
                before,
                after),
            cancellationToken);
    }
}
