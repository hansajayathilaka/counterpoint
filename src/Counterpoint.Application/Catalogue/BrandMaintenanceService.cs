using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>The owner's brand maintenance (SRS FR-2.21).</summary>
internal sealed class BrandMaintenanceService : IBrandMaintenance
{
    private readonly IBrandStore _store;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public BrandMaintenanceService(
        IBrandStore store,
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
    public Task<IReadOnlyList<BrandRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateAsync(SaveBrandCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = RequireName(command.Name);

        if (await _store.ExistsWithNameAsync(name, excludingId: null, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a brand called '{name}'. Pick another name."));
        }

        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _store.CreateAsync(name, token).ConfigureAwait(false);

                await RecordAsync(CatalogueAuditActions.Created, id, now, before: null, after: Json(name), token)
                    .ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(long id, SaveBrandCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var existing = await RequireBrandAsync(id, cancellationToken).ConfigureAwait(false);
        var name = RequireName(command.Name);

        if (await _store.ExistsWithNameAsync(name, excludingId: id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a brand called '{name}'. Pick another name."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateAsync(id, name, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated, id, now, before: Json(existing.Name), after: Json(name), token)
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireBrandAsync(id, cancellationToken).ConfigureAwait(false);

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
        var existing = await RequireBrandAsync(id, cancellationToken).ConfigureAwait(false);

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
        var existing = await RequireBrandAsync(id, cancellationToken).ConfigureAwait(false);

        if (await _store.HasProductsAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"'{existing.Name}' has products carrying it and cannot be deleted. Deactivate it instead."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (!await _store.DeleteAsync(id, token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.CurrentCulture,
                        $"'{existing.Name}' is referenced elsewhere and cannot be deleted. Deactivate it instead."));
                }

                await RecordAsync(CatalogueAuditActions.Deleted, id, now, before: Json(existing.Name), after: null, token)
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
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

    private async Task<BrandRecord> RequireBrandAsync(long id, CancellationToken cancellationToken) =>
        await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.CurrentCulture,
            $"There is no brand with id {id}. It may have been removed since this screen was opened."));

    private static string RequireName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A brand needs a name.");
        }

        return trimmed;
    }

    private static string Json(string name) => SecurityAuditJson.Object(("name", name));

    private Task RecordAsync(
        string action,
        long brandId,
        DateTimeOffset now,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Brand maintenance ran without a session. The role decorator should have refused this "
            + "call; the service is registered without it.");

        return _audit.RecordAsync(
            new AuditEntry(now, actor.Id, action, CatalogueAuditActions.BrandEntityType, brandId, before, after),
            cancellationToken);
    }
}
