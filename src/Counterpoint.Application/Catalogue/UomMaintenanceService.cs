using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// The owner's unit-of-measure maintenance.
/// </summary>
/// <remarks>
/// No deactivate, no reactivate: see the remarks on <see cref="UomRecord"/>. Delete is the only
/// way to retire a unit, and it is refused while any product still references it.
/// </remarks>
internal sealed class UomMaintenanceService : IUomMaintenance
{
    private readonly IUomStore _store;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public UomMaintenanceService(
        IUomStore store,
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
    public Task<IReadOnlyList<UomRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _store.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateAsync(SaveUomCommand command, CancellationToken cancellationToken = default)
    {
        var (name, symbol, decimalPlaces) = Validate(command);

        if (await _store.ExistsWithNameAsync(name, excludingId: null, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a unit called '{name}'. Pick another name."));
        }

        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var id = await _store.CreateAsync(name, symbol, decimalPlaces, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Created, id, now, before: null, after: Json(name, symbol, decimalPlaces),
                    token).ConfigureAwait(false);

                return id;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(long id, SaveUomCommand command, CancellationToken cancellationToken = default)
    {
        var existing = await RequireUomAsync(id, cancellationToken).ConfigureAwait(false);
        var (name, symbol, decimalPlaces) = Validate(command);

        if (await _store.ExistsWithNameAsync(name, excludingId: id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a unit called '{name}'. Pick another name."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _store.UpdateAsync(id, name, symbol, decimalPlaces, token).ConfigureAwait(false);

                await RecordAsync(
                    CatalogueAuditActions.Updated,
                    id,
                    now,
                    before: Json(existing.Name, existing.Symbol, existing.DecimalPlaces),
                    after: Json(name, symbol, decimalPlaces),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
    {
        var existing = await RequireUomAsync(id, cancellationToken).ConfigureAwait(false);

        if (await _store.HasProductsAsync(id, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"'{existing.Name}' is used by one or more products and cannot be deleted."));
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (!await _store.DeleteAsync(id, token).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.CurrentCulture,
                        $"'{existing.Name}' is referenced elsewhere and cannot be deleted."));
                }

                await RecordAsync(
                    CatalogueAuditActions.Deleted,
                    id,
                    now,
                    before: Json(existing.Name, existing.Symbol, existing.DecimalPlaces),
                    after: null,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<UomRecord> RequireUomAsync(long id, CancellationToken cancellationToken) =>
        await _store.FindByIdAsync(id, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.CurrentCulture,
            $"There is no unit with id {id}. It may have been removed since this screen was opened."));

    private static (string Name, string Symbol, int DecimalPlaces) Validate(SaveUomCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var name = command.Name?.Trim() ?? string.Empty;
        var symbol = command.Symbol?.Trim() ?? string.Empty;

        if (name.Length == 0)
        {
            throw new InvalidOperationException("A unit needs a name.");
        }

        if (symbol.Length == 0)
        {
            throw new InvalidOperationException("A unit needs a symbol, such as 'pc' or 'm'.");
        }

        if (command.DecimalPlaces is < 0 or > 4)
        {
            throw new InvalidOperationException(
                "A unit's decimal places must be between 0 and 4 (ck_uom_decimal_places).");
        }

        return (name, symbol, command.DecimalPlaces);
    }

    private static string Json(string name, string symbol, int decimalPlaces) =>
        SecurityAuditJson.Object(("name", name), ("symbol", symbol), ("decimal_places", (long)decimalPlaces));

    private Task RecordAsync(
        string action,
        long uomId,
        DateTimeOffset now,
        string? before,
        string? after,
        CancellationToken cancellationToken)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "Unit maintenance ran without a session. The role decorator should have refused this "
            + "call; the service is registered without it.");

        return _audit.RecordAsync(
            new AuditEntry(now, actor.Id, action, CatalogueAuditActions.UomEntityType, uomId, before, after),
            cancellationToken);
    }
}
