using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;
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
/// </para>
/// <para>
/// The moving-average cost math itself is <see cref="StockLedgerMath"/> - the very function
/// <c>RebuildStockBalanceCommand</c> replays, so posting one movement here and replaying it
/// later land on the same number (P1-T07).
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

                var baseUomId = posting.QuantityBase.UomId;
                var qtyBefore = projection is null
                    ? Quantity.Zero(baseUomId)
                    : Quantity.FromScaled(projection.QtyBase, baseUomId);
                var costAvgBefore = projection?.CostAvg ?? Money.Zero;

                var step = StockLedgerMath.Apply(qtyBefore, costAvgBefore, posting.QuantityBase, posting.UnitCost);
                var balanceAfter = step.QtyAfter.ToScaled();

                context.Add(new StockMovement
                {
                    ProductVariantId = posting.ProductVariantId,
                    MovementType = posting.MovementType,
                    QtyBase = posting.QuantityBase.ToScaled(),
                    UnitCost = step.MovementUnitCost,
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
                        CostAvg = step.CostAvgAfter,
                        UpdatedAt = posting.OccurredAt,
                    });
                }
                else
                {
                    projection.QtyBase = balanceAfter;
                    projection.CostAvg = step.CostAvgAfter;
                    projection.UpdatedAt = posting.OccurredAt;
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);
    }
}
