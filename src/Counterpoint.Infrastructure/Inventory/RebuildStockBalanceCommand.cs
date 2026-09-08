using System;
using System.Collections.Generic;
using System.Linq;
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
/// Replays every ledger row into the balance projection, replacing whatever is there now
/// (P1-T07, CLAUDE.md invariant 3).
/// </summary>
/// <remarks>
/// <para>
/// Runs <see cref="StockLedgerMath.Apply"/> - the same function <see cref="SqliteStockLedger"/>
/// runs on every post - once per movement, per variant, in the order the movements were
/// originally posted (surrogate id ascending, which is insertion order on an append-only table).
/// Same function, same inputs, same order: the rebuilt projection is not merely close to what
/// posting them one at a time would have produced, it is the same number, to the scaled integer.
/// </para>
/// <para>
/// One of the two places allowed to write the projection directly - the other is
/// <see cref="SqliteStockLedger"/> itself - which is what keeps this a second door onto the same
/// room rather than a second room (see the architecture test that polices this).
/// </para>
/// </remarks>
internal sealed class RebuildStockBalanceCommand : IRebuildStockBalance
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public RebuildStockBalanceCommand(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<StockBalanceRebuildSummary> RebuildAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                // Insertion order, per variant: an append-only table's surrogate id increases
                // with every insert, so ordering by it reproduces the sequence PostAsync
                // actually ran in.
                var movements = await context.Set<StockMovement>()
                    .OrderBy(movement => movement.ProductVariantId)
                    .ThenBy(movement => movement.Id)
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                var projections = await context.Set<StockBalance>()
                    .ToDictionaryAsync(row => row.ProductVariantId, token)
                    .ConfigureAwait(false);

                var variantCount = 0;

                foreach (var group in movements.GroupBy(movement => movement.ProductVariantId))
                {
                    var variantId = group.Key;

                    // Quantity.Value is scale-only arithmetic here - a variant's base unit never
                    // changes, and StockLedgerMath.Apply only ever needs a consistent uom tag to
                    // satisfy Quantity's same-unit guard, not the variant's true base uom id.
                    var qty = Quantity.Zero(variantId);
                    var costAvg = Money.Zero;
                    var updatedAt = default(DateTimeOffset);

                    foreach (var movement in group)
                    {
                        var movementQty = Quantity.FromScaled(movement.QtyBase, variantId);
                        var step = StockLedgerMath.Apply(qty, costAvg, movementQty, movement.UnitCost);

                        // Quantised after every step, not only once at the end. This is not
                        // optional precision-matching: SqliteStockLedger.PostAsync re-reads the
                        // projection from the database before every post, and the database only
                        // ever holds the scaled-integer form - so production's running average
                        // is rounded to the storage scale after every single movement, and a
                        // replay that kept full decimal precision across many movements would
                        // drift from it by the accumulated rounding.
                        qty = Quantity.FromScaled(step.QtyAfter.ToScaled(), variantId);
                        costAvg = Money.FromScaled(step.CostAvgAfter.ToScaled());
                        updatedAt = movement.OccurredAt;
                    }

                    if (projections.TryGetValue(group.Key, out var projection))
                    {
                        projection.QtyBase = qty.ToScaled();
                        projection.CostAvg = costAvg;
                        projection.UpdatedAt = updatedAt;
                    }
                    else
                    {
                        context.Add(new StockBalance
                        {
                            ProductVariantId = group.Key,
                            QtyBase = qty.ToScaled(),
                            CostAvg = costAvg,
                            UpdatedAt = updatedAt,
                        });
                    }

                    variantCount++;
                }

                await context.SaveChangesAsync(token).ConfigureAwait(false);

                return new StockBalanceRebuildSummary(variantCount, movements.Count);
            },
            cancellationToken);
}
