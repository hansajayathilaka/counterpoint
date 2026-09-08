using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// <c>tax_class</c>, read and written through the unit of work for the owner's ongoing
/// maintenance screen (Q-02, FR-10.3). See <see cref="ITaxClassStore"/> for why this is separate
/// from <see cref="ITaxClassSeed"/>.
/// </summary>
internal sealed class SqliteTaxClassStore : ITaxClassStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteTaxClassStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TaxClassRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<TaxClass>().OrderBy(row => row.Name).ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<TaxClassRecord> result = [.. rows.Select(ToRecord)];
                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<TaxClassRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                var row = await context.Set<TaxClass>().FirstOrDefaultAsync(t => t.Id == id, token)
                    .ConfigureAwait(false);
                return row is null ? null : ToRecord(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsWithNameAsync(
        string name,
        long? excludingId,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                return await context.Set<TaxClass>().AnyAsync(
                    row => row.Name == name && (excludingId == null || row.Id != excludingId), token)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                return await context.Set<Product>().AnyAsync(row => row.TaxClassId == id, token)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(string name, TaxRate rate, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new TaxClass { Name = name, Rate = rate, Active = true };
                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(long id, string name, TaxRate rate, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<TaxClass>().FirstOrDefaultAsync(t => t.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no tax_class row with id {id}.");

                row.Name = name;
                row.Rate = rate;

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<TaxClass>().FirstOrDefaultAsync(t => t.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no tax_class row with id {id}.");

                row.Active = active;
                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<TaxClass>().FirstOrDefaultAsync(t => t.Id == id, token)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return true;
                }

                context.Remove(row);

                try
                {
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    return true;
                }
                catch (DbUpdateException exception) when (SqliteErrors.IsForeignKeyViolation(exception))
                {
                    return false;
                }
            },
            cancellationToken);

    private static TaxClassRecord ToRecord(TaxClass row) => new(row.Id, row.Name, row.Rate, row.Active);
}
