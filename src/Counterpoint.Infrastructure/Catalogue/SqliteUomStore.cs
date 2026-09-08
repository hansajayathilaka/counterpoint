using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// <c>uom</c>, read and written through the unit of work. See the remarks on
/// <see cref="UomRecord"/> for why there is no <c>SetActiveAsync</c> here: the table has no
/// <c>active</c> column.
/// </summary>
internal sealed class SqliteUomStore : IUomStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteUomStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UomRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<Uom>().OrderBy(row => row.Name).ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<UomRecord> result = [.. rows.Select(ToRecord)];
                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<UomRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                var row = await context.Set<Uom>().FirstOrDefaultAsync(u => u.Id == id, token)
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
                return await context.Set<Uom>().AnyAsync(
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

                var isBaseUnit = await context.Set<Product>().AnyAsync(row => row.BaseUomId == id, token)
                    .ConfigureAwait(false);

                if (isBaseUnit)
                {
                    return true;
                }

                return await context.Set<ProductUom>().AnyAsync(row => row.UomId == id, token)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(
        string name,
        string symbol,
        int decimalPlaces,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new Uom { Name = name, Symbol = symbol, DecimalPlaces = decimalPlaces };
                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(
        long id,
        string name,
        string symbol,
        int decimalPlaces,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Uom>().FirstOrDefaultAsync(u => u.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no uom row with id {id}.");

                row.Name = name;
                row.Symbol = symbol;
                row.DecimalPlaces = decimalPlaces;

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

                var row = await context.Set<Uom>().FirstOrDefaultAsync(u => u.Id == id, token)
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

    private static UomRecord ToRecord(Uom row) => new(row.Id, row.Name, row.Symbol, row.DecimalPlaces);
}
