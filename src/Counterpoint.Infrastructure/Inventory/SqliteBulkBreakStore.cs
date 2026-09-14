using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;

namespace Counterpoint.Infrastructure.Inventory;

/// <summary>
/// <c>bulk_break</c>, read and written through the unit of work (docs/01_DATA_MODEL.md §4, SRS
/// FR-4.9, AC-09, task P2-T09).
/// </summary>
/// <remarks>
/// Not append-only (CLAUDE.md invariant 5 names exactly which tables are; <c>bulk_break</c> is
/// not among them - the same shape as <c>SqliteGoodsReceiptStore</c>). <see cref="CreateAsync"/>
/// is the only write: nothing about a posted bulk break is ever edited, the same "correct with a
/// fresh document, never a rewrite" discipline every other posted document in this codebase keeps.
/// </remarks>
internal sealed class SqliteBulkBreakStore : IBulkBreakStore
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteBulkBreakStore(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<long> CreateAsync(NewBulkBreak bulkBreak, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bulkBreak);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var row = new BulkBreak
                {
                    SourceVariantId = bulkBreak.SourceVariantId,
                    DestinationVariantId = bulkBreak.DestinationVariantId,
                    SourceQtyBase = bulkBreak.SourceQtyBase.ToScaled(),
                    ExpectedQtyBase = bulkBreak.ExpectedQtyBase.ToScaled(),
                    ActualQtyBase = bulkBreak.ActualQtyBase.ToScaled(),
                    WastageQtyBase = bulkBreak.WastageQtyBase.ToScaled(),
                    TotalValue = bulkBreak.TotalValue,
                    Reason = bulkBreak.Reason,
                    UserId = bulkBreak.UserId,
                    OccurredAt = bulkBreak.OccurredAt,
                };

                context.Add(row);
                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return row.Id;
            },
            cancellationToken);
    }
}
