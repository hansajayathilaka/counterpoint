using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Inventory;

/// <summary>
/// Appends to the stock ledger and advances its projection, in one transaction
/// (CLAUDE.md invariant 3).
/// </summary>
/// <remarks>
/// <para>
/// <c>balance_after</c> is computed from the projection read inside this transaction, never
/// from summing the ledger: the sum is O(history) and would grow slower with every bill ever
/// rung up, on the sale path.
/// </para>
/// <para>
/// The projection is created on first movement if it is missing, so a variant that has never
/// been counted still gets an honest balance rather than a foreign-key error at the till.
/// Moving-average cost is left alone here - it is a purchase-side calculation, and it arrives
/// with goods receipt in P2-T07.
/// </para>
/// </remarks>
internal sealed class SqliteStockLedger : IStockLedger
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteStockLedger(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task PostAsync(StockPosting posting, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(posting);

        return _unitOfWork.ExecuteInTransactionAsync<object?>(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var projection = await context.Set<StockBalance>()
                    .FirstOrDefaultAsync(row => row.ProductVariantId == posting.ProductVariantId, token)
                    .ConfigureAwait(false);

                var movedBy = posting.QuantityBase.ToScaled();
                var balanceAfter = checked((projection?.QtyBase ?? 0L) + movedBy);

                context.Add(new StockMovement
                {
                    ProductVariantId = posting.ProductVariantId,
                    MovementType = posting.MovementType,
                    QtyBase = movedBy,
                    UnitCost = posting.UnitCost,
                    RefDocType = posting.RefDocType,
                    RefDocId = posting.RefDocId,
                    BalanceAfter = balanceAfter,
                    UserId = posting.UserId,
                    OccurredAt = posting.OccurredAt,
                    Note = posting.Note,
                });

                if (projection is null)
                {
                    context.Add(new StockBalance
                    {
                        ProductVariantId = posting.ProductVariantId,
                        QtyBase = balanceAfter,
                        CostAvg = posting.UnitCost,
                        UpdatedAt = posting.OccurredAt,
                    });
                }
                else
                {
                    projection.QtyBase = balanceAfter;
                    projection.UpdatedAt = posting.OccurredAt;
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);
    }
}
