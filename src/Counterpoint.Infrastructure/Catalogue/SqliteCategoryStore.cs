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
/// <c>category</c>, read and written through the unit of work (SRS FR-2.20, FR-2.21).
/// </summary>
/// <remarks>
/// Every method - reads included - runs inside <see cref="SqliteUnitOfWork.ExecuteInTransactionAsync{TResult}(Func{CancellationToken,Task{TResult}},CancellationToken)"/>,
/// the same shape <c>SqliteTaxClassSeed</c> uses. This is reference-data administration, not a
/// hot path (NFR-P1 does not apply here), so the simplicity of one connection pattern for every
/// method wins over shaving a read off the writer lock. A call made from inside an already-open
/// business transaction joins it rather than opening a second one (re-entrant by design, see
/// <see cref="SqliteUnitOfWork"/>).
/// </remarks>
internal sealed class SqliteCategoryStore : ICategoryStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteCategoryStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CategoryRecord>> ListAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<Category>()
                    .OrderBy(row => row.ParentId)
                    .ThenBy(row => row.Name)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var byId = rows.ToDictionary(row => row.Id);

                IReadOnlyList<CategoryRecord> result = [.. rows.Select(row => ToRecord(row, byId))];
                return result;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<CategoryRecord?> FindByIdAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Category>().FirstOrDefaultAsync(c => c.Id == id, token)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return null;
                }

                Category? parent = row.ParentId is { } parentId
                    ? await context.Set<Category>().FirstOrDefaultAsync(c => c.Id == parentId, token)
                        .ConfigureAwait(false)
                    : null;

                return ToRecord(row, parent);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> ExistsWithNameAsync(
        string name,
        long? parentId,
        long? excludingId,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                return await context.Set<Category>().AnyAsync(
                    row => row.Name == name
                        && row.ParentId == parentId
                        && (excludingId == null || row.Id != excludingId),
                    token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasChildrenAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                return await context.Set<Category>().AnyAsync(row => row.ParentId == id, token)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasProductsAsync(long id, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();
                return await context.Set<Product>().AnyAsync(row => row.CategoryId == id, token)
                    .ConfigureAwait(false);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<long> CreateAsync(string name, long? parentId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new Category { Name = name, ParentId = parentId, Active = true };

                context.Add(row);
                await SaveGuardingTwoLevelsAsync(context, token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(long id, string name, long? parentId, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Category>().FirstOrDefaultAsync(c => c.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no category row with id {id}.");

                row.Name = name;
                row.ParentId = parentId;

                await SaveGuardingTwoLevelsAsync(context, token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task SetActiveAsync(long id, bool active, CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = await context.Set<Category>().FirstOrDefaultAsync(c => c.Id == id, token)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"There is no category row with id {id}.");

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

                var row = await context.Set<Category>().FirstOrDefaultAsync(c => c.Id == id, token)
                    .ConfigureAwait(false);

                if (row is null)
                {
                    return true;
                }

                context.Remove(row);
                return await TryDeleteAsync(context, token).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>
    /// Saves, translating the backstop trigger's raw message into the same sentence the
    /// application-layer pre-checks already give for the common case (SRS FR-2.20).
    /// </summary>
    private static async Task SaveGuardingTwoLevelsAsync(PosDbContext context, CancellationToken token)
    {
        try
        {
            await context.SaveChangesAsync(token).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (SqliteErrors.RaisedByTrigger(exception, "two levels only"))
        {
            throw new InvalidOperationException(
                "A category can only be two levels deep.", exception);
        }
    }

    /// <summary>
    /// Saves a delete, turning a foreign-key refusal into <see langword="false"/> rather than
    /// letting the raw <see cref="DbUpdateException"/> escape - the backstop for a reference this
    /// store's own pre-checks did not see.
    /// </summary>
    private static async Task<bool> TryDeleteAsync(PosDbContext context, CancellationToken token)
    {
        try
        {
            await context.SaveChangesAsync(token).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException exception) when (SqliteErrors.IsForeignKeyViolation(exception))
        {
            return false;
        }
    }

    private static CategoryRecord ToRecord(Category row, Dictionary<long, Category> byId) =>
        ToRecord(row, row.ParentId is { } parentId && byId.TryGetValue(parentId, out var parent) ? parent : null);

    private static CategoryRecord ToRecord(Category row, Category? parent) =>
        new(row.Id, row.Name, row.ParentId, parent?.Name, row.Active);
}
