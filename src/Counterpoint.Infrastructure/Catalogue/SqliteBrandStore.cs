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

/// <summary><c>brand</c>, read and written through the unit of work (SRS FR-2.21).</summary>
internal sealed class SqliteBrandStore : IBrandStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteBrandStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BrandRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<Brand>().OrderBy(row => row.Name).ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<BrandRecord> result = [.. rows.Select(ToRecord)];
                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<BrandRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                var row = await context.Set<Brand>().FirstOrDefaultAsync(b => b.Id == id, token)
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
                return await context.Set<Brand>().AnyAsync(
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
                return await context.Set<Product>().AnyAsync(row => row.BrandId == id, token)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(string name, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new Brand { Name = name, Active = true };
                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(long id, string name, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Brand>().FirstOrDefaultAsync(b => b.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no brand row with id {id}.");

                row.Name = name;
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

                var row = await context.Set<Brand>().FirstOrDefaultAsync(b => b.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no brand row with id {id}.");

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

                var row = await context.Set<Brand>().FirstOrDefaultAsync(b => b.Id == id, token)
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

    private static BrandRecord ToRecord(Brand row) => new(row.Id, row.Name, row.Active);
}
