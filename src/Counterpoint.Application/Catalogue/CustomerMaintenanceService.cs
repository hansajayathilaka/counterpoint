using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>The owner's customer maintenance (SRS FR-6.1).</summary>
internal sealed class CustomerMaintenanceService : ICustomerMaintenance
{
    private readonly ICustomerStore _store;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public CustomerMaintenanceService(
        ICustomerStore store,
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
    public Task<IReadOnlyList<CustomerRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateAsync(SaveCustomerCommand command, CancellationToken cancellationToken = default)
    {
        var customer = Validate(command);
        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _store.CreateAsync(customer, token).ConfigureAwait(false);

                await RecordAsync(CatalogueAuditActions.Created, id, now, before: null, after: Json(customer), token)
                    .ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(long id, SaveCustomerCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await RequireCustomerAsync(id, cancellationToken).ConfigureAwait(false);
        var customer = Validate(command);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateAsync(id, customer, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    id,
                    now,
                    before: Json(new NewCustomer(
                        existing.Name, existing.Phone, existing.Address, existing.TaxNo, existing.Type,
                        existing.CreditLimit)),
                    after: Json(customer),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireCustomerAsync(id, cancellationToken).ConfigureAwait(false);

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
        var existing = await RequireCustomerAsync(id, cancellationToken).ConfigureAwait(false);

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
        var existing = await RequireCustomerAsync(id, cancellationToken).ConfigureAwait(false);

        if (await _store.HasSalesAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"'{existing.Name}' has bills recorded against them and cannot be deleted. Deactivate instead."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (!await _store.DeleteAsync(id, token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.CurrentCulture,
                        $"'{existing.Name}' is referenced elsewhere and cannot be deleted. Deactivate instead."));
                }

                await RecordAsync(
                    CatalogueAuditActions.Deleted,
                    id,
                    now,
                    before: Json(new NewCustomer(
                        existing.Name, existing.Phone, existing.Address, existing.TaxNo, existing.Type,
                        existing.CreditLimit)),
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

    private async Task<CustomerRecord> RequireCustomerAsync(long id, CancellationToken cancellationToken) =>
        await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.CurrentCulture,
            $"There is no customer with id {id}. It may have been removed since this screen was opened."));

    private static NewCustomer Validate(SaveCustomerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            throw new InvalidOperationException("A customer needs a name.");
        }

        var type = command.Type?.Trim().ToUpperInvariant() ?? string.Empty;
        if (type is not ("RETAIL" or "TRADE"))
        {
            throw new InvalidOperationException("A customer's type must be Retail or Trade (ck_customer_type).");
        }

        if (command.CreditLimit.Amount < 0m)
        {
            throw new InvalidOperationException("A credit limit cannot be negative.");
        }

        return new NewCustomer(name, Trimmed(command.Phone), Trimmed(command.Address), Trimmed(command.TaxNo), type,
            command.CreditLimit);
    }

    private static string? Trimmed(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string Json(NewCustomer customer) => SecurityAuditJson.Object(
        ("name", customer.Name),
        ("phone", customer.Phone),
        ("address", customer.Address),
        ("tax_no", customer.TaxNo),
        ("type", customer.Type),
        ("credit_limit", customer.CreditLimit.ToScaled()));

    private Task RecordAsync(
        string action,
        long customerId,
        DateTimeOffset now,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Customer maintenance ran without a session. The role decorator should have refused "
            + "this call; the service is registered without it.");

        return _audit.RecordAsync(
            new AuditEntry(now, actor.Id, action, CatalogueAuditActions.CustomerEntityType, customerId, before, after),
            cancellationToken);
    }
}
