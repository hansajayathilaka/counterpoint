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
/// <c>price_change_log</c>, read and written through the unit of work (docs/01_DATA_MODEL.md §3,
/// SRS FR-2.17).
/// </summary>
internal sealed class SqlitePriceChangeLogStore : IPriceChangeLogStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqlitePriceChangeLogStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task RecordAsync(NewPriceChangeLogEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                context.Add(new PriceChangeLog
                {
                    ProductVariantId = entry.ProductVariantId,
                    OldPrice = entry.OldPrice,
                    NewPrice = entry.NewPrice,
                    ChangedAt = entry.ChangedAt,
                    UserId = entry.UserId,
                    Reason = entry.Reason,
                });

                await context.SaveChangesAsync(token).ConfigureAwait(false);
                return null;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PriceChangeLogEntry>> ListByVariantAsync(
        long productVariantId,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var rows = await context.Set<PriceChangeLog>()
                    .Where(row => row.ProductVariantId == productVariantId)
                    .OrderByDescending(row => row.ChangedAt)
                    .ThenByDescending(row => row.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<PriceChangeLogEntry> result = [.. rows.Select(row => new PriceChangeLogEntry(
                    row.Id,
                    row.ProductVariantId,
                    row.OldPrice,
                    row.NewPrice,
                    row.ChangedAt,
                    row.UserId,
                    row.Reason))];

                return result;
            },
            cancellationToken);
}
