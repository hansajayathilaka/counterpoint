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
/// <para>
/// <b>An inbound movement also updates <c>product.cost_avg</c></b> - the column the catalogue's
/// below-cost pricing guard reads (<c>ProductMaintenanceService.RequireAboveCostOrConfirmed</c>,
/// SRS FR-2.18), which its own remarks document as meant to be "built up by stock receipts". This
/// is the one and only place that happens: every GRN, sale return, positive adjustment, stock
/// take and bulk-break destination posts through here, so the guard's cost figure moves the same
/// moment the projection's own average does, rather than staying frozen wherever the product was
/// created. Gated on <c>posting.QuantityBase.IsPositive</c> - the same condition under which
/// <see cref="StockLedgerMath.Apply"/> itself recomputes the average - so an outbound sale line,
/// the hottest path through here, never pays for the extra lookup.
/// </para>
/// <para>
/// <b>One column, potentially several variants.</b> <c>product.cost_avg</c> lives on the product,
/// but the moving average it is copied from is computed per variant
/// (<see cref="StockLedgerMath.Apply"/> against that variant's own projection). For a product with
/// more than one variant, this column ends up holding whichever variant posted an inbound movement
/// most recently - never a true blended figure across them. That is acceptable for what it is
/// used for: a rough starting guide for a brand-new variant that has no stock history of its own
/// yet, and FR-2.18 makes the check it feeds a warning, not a block. It is never read on the sale
/// path - <c>sale_line.unit_cost</c> always snapshots the posting variant's own average, from the
/// projection this class maintains (<c>SqliteProductLookup</c>'s own P1-T10 remarks).
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

                // See the class remarks: only an inbound movement recomputed the average, so only
                // an inbound movement has anything new to tell the below-cost guard.
                if (posting.QuantityBase.IsPositive)
                {
                    var productId = await context.Set<ProductVariant>()
                        .Where(variant => variant.Id == posting.ProductVariantId)
                        .Select(variant => variant.ProductId)
                        .FirstOrDefaultAsync(token)
                        .ConfigureAwait(false);

                    var product = await context.Set<Product>()
                        .FirstOrDefaultAsync(row => row.Id == productId, token)
                        .ConfigureAwait(false);

                    if (product is not null)
                    {
                        product.CostAvg = step.CostAvgAfter;
                    }
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return null;
            },
            cancellationToken);
    }
}
