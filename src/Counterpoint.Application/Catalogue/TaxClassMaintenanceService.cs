using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>The owner's ongoing tax-class maintenance, after the first-run wizard (Q-02, FR-10.3).</summary>
internal sealed class TaxClassMaintenanceService : ITaxClassMaintenance
{
    private readonly ITaxClassStore _store;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public TaxClassMaintenanceService(
        ITaxClassStore store,
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
    public Task<IReadOnlyList<TaxClassRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateAsync(SaveTaxClassCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var name = RequireName(command.Name);

        if (await _store.ExistsWithNameAsync(name, excludingId: null, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a tax class called '{name}'. Pick another name."));
        }

        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _store.CreateAsync(name, command.Rate, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Created, id, now, before: null, after: Json(name, command.Rate), token)
                    .ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(long id, SaveTaxClassCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var existing = await RequireTaxClassAsync(id, cancellationToken).ConfigureAwait(false);
        var name = RequireName(command.Name);

        if (await _store.ExistsWithNameAsync(name, excludingId: id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a tax class called '{name}'. Pick another name."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateAsync(id, name, command.Rate, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    id,
                    now,
                    before: Json(existing.Name, existing.Rate),
                    after: Json(name, command.Rate),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireTaxClassAsync(id, cancellationToken).ConfigureAwait(false);

        if (!existing.Active)
        {
            return;
        }

        await SetActiveAsync(id, existing.Name, existing.Rate, active: false, CatalogueAuditActions.Deactivated, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireTaxClassAsync(id, cancellationToken).ConfigureAwait(false);

        if (existing.Active)
        {
            return;
        }

        await SetActiveAsync(id, existing.Name, existing.Rate, active: true, CatalogueAuditActions.Reactivated, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireTaxClassAsync(id, cancellationToken).ConfigureAwait(false);

        if (await _store.HasProductsAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"'{existing.Name}' is used by one or more products and cannot be deleted. Deactivate it instead."));
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

                await RecordAsync(
                    CatalogueAuditActions.Deleted, id, now, before: Json(existing.Name, existing.Rate), after: null, token)
                    .ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SetActiveAsync(
        long id,
        string name,
        TaxRate rate,
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
                    before: SecurityAuditJson.Object(("name", name), ("rate", rate.ToScaled()), ("active", !active)),
                    after: SecurityAuditJson.Object(("name", name), ("rate", rate.ToScaled()), ("active", active)),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TaxClassRecord> RequireTaxClassAsync(long id, CancellationToken cancellationToken) =>
        await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.CurrentCulture,
            $"There is no tax class with id {id}. It may have been removed since this screen was opened."));

    private static string RequireName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var trimmed = name.Trim();

        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("A tax class needs a name.");
        }

        return trimmed;
    }

    private static string Json(string name, TaxRate rate) =>
        SecurityAuditJson.Object(("name", name), ("rate", rate.ToScaled()));

    private Task RecordAsync(
        string action,
        long taxClassId,
        DateTimeOffset now,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Tax class maintenance ran without a session. The role decorator should have refused "
            + "this call; the service is registered without it.");

        return _audit.RecordAsync(
            new AuditEntry(now, actor.Id, action, CatalogueAuditActions.TaxClassEntityType, taxClassId, before, after),
            cancellationToken);
    }
}
