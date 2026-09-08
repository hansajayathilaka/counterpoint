using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>The owner's supplier maintenance (SRS FR-6.5).</summary>
internal sealed class SupplierMaintenanceService : ISupplierMaintenance
{
    private readonly ISupplierStore _store;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public SupplierMaintenanceService(
        ISupplierStore store,
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
    public Task<IReadOnlyList<SupplierRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateAsync(SaveSupplierCommand command, CancellationToken cancellationToken = default)
    {
        var supplier = Validate(command);
        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _store.CreateAsync(supplier, token).ConfigureAwait(false);

                await RecordAsync(CatalogueAuditActions.Created, id, now, before: null, after: Json(supplier), token)
                    .ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(long id, SaveSupplierCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await RequireSupplierAsync(id, cancellationToken).ConfigureAwait(false);
        var supplier = Validate(command);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateAsync(id, supplier, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    id,
                    now,
                    before: Json(new NewSupplier(
                        existing.Name, existing.Contact, existing.Phone, existing.Address, existing.TaxNo,
                        existing.PaymentTerms)),
                    after: Json(supplier),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireSupplierAsync(id, cancellationToken).ConfigureAwait(false);

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
        var existing = await RequireSupplierAsync(id, cancellationToken).ConfigureAwait(false);

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
        var existing = await RequireSupplierAsync(id, cancellationToken).ConfigureAwait(false);

        if (await _store.HasLinksAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"'{existing.Name}' is linked to a product, purchase order or goods receipt and cannot be deleted. Deactivate it instead."));
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
                    CatalogueAuditActions.Deleted,
                    id,
                    now,
                    before: Json(new NewSupplier(
                        existing.Name, existing.Contact, existing.Phone, existing.Address, existing.TaxNo,
                        existing.PaymentTerms)),
                    after: null,
                    token).ConfigureAwait(false);
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

    private async Task<SupplierRecord> RequireSupplierAsync(long id, CancellationToken cancellationToken) =>
        await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.CurrentCulture,
            $"There is no supplier with id {id}. It may have been removed since this screen was opened."));

    private static NewSupplier Validate(SaveSupplierCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("A supplier needs a name.");
        }

        return new NewSupplier(
            name,
            Trimmed(command.Contact),
            Trimmed(command.Phone),
            Trimmed(command.Address),
            Trimmed(command.TaxNo),
            Trimmed(command.PaymentTerms));
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string Json(NewSupplier supplier) => SecurityAuditJson.Object(
        ("name", supplier.Name),
        ("contact", supplier.Contact),
        ("phone", supplier.Phone),
        ("address", supplier.Address),
        ("tax_no", supplier.TaxNo),
        ("payment_terms", supplier.PaymentTerms));

    private Task RecordAsync(
        string action,
        long supplierId,
        DateTimeOffset now,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Supplier maintenance ran without a session. The role decorator should have refused "
            + "this call; the service is registered without it.");

        return _audit.RecordAsync(
            new AuditEntry(now, actor.Id, action, CatalogueAuditActions.SupplierEntityType, supplierId, before, after),
            cancellationToken);
    }
}
