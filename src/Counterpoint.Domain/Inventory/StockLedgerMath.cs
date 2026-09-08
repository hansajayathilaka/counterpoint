using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Inventory;

/// <summary>
/// One movement applied to a running stock position: the quantity after, the moving-average cost
/// after, and the cost the movement itself is recorded at.
/// </summary>
/// <param name="QtyAfter">The running quantity, in base units, after the movement.</param>
/// <param name="CostAvgAfter">The moving-average cost after the movement.</param>
/// <param name="MovementUnitCost">
/// What the ledger row's own <c>unit_cost</c> should read: the cost that arrived, for an inbound
/// movement, or the cost that was already on the shelf, for an outbound one (P1-T07).
/// </param>
public readonly record struct StockLedgerStep(Quantity QtyAfter, Money CostAvgAfter, Money MovementUnitCost);

/// <summary>
/// Advances a stock position by one signed movement (P1-T07, SRS FR-4, DM-05).
/// </summary>
/// <remarks>
/// <para>
/// The single step both <c>StockLedger.PostAsync</c> and the rebuild command run, so that
/// replaying every movement for a variant, in the order they were posted, reproduces the
/// projection exactly - the same function, the same inputs in the same order, the same output.
/// </para>
/// <para>
/// Pure and framework-free: everything it needs is <see cref="Quantity"/> and <see cref="Money"/>,
/// so it is fully provable without a database.
/// </para>
/// </remarks>
public static class StockLedgerMath
{
    /// <summary>
    /// Applies one signed movement to a running stock position.
    /// </summary>
    /// <param name="qtyBefore">The running quantity, in base units, before the movement.</param>
    /// <param name="costAvgBefore">The moving-average cost before the movement.</param>
    /// <param name="movementQtyBase">
    /// The movement's own signed quantity, in base units. Positive is inbound (a receipt, a
    /// return coming back in, a positive count or adjustment) and recomputes the moving-average
    /// cost against <paramref name="movementUnitCost"/>. Negative is outbound (a sale, a
    /// write-off, a negative count or adjustment) and leaves the average untouched - it snapshots
    /// <paramref name="costAvgBefore"/> as the COGS instead of whatever the caller passed in.
    /// </param>
    /// <param name="movementUnitCost">
    /// The cost of what arrived. Used only when <paramref name="movementQtyBase"/> is positive;
    /// ignored for an outbound movement, because outbound COGS is what was already on the shelf,
    /// never a value the caller supplies.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="movementQtyBase"/> is zero.</exception>
    public static StockLedgerStep Apply(
        Quantity qtyBefore,
        Money costAvgBefore,
        Quantity movementQtyBase,
        Money movementUnitCost)
    {
        if (movementQtyBase.IsZero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(movementQtyBase),
                movementQtyBase,
                "A stock movement must move a non-zero quantity.");
        }

        var qtyAfter = qtyBefore + movementQtyBase;

        if (movementQtyBase.IsPositive)
        {
            var costAvgAfter = MovingAverageCost.Recompute(qtyBefore, costAvgBefore, movementQtyBase, movementUnitCost);
            return new StockLedgerStep(qtyAfter, costAvgAfter, movementUnitCost);
        }

        // Outbound: the COGS snapshot is whatever is on the shelf right now. The average itself
        // never moves on the way out (P1-T07).
        return new StockLedgerStep(qtyAfter, costAvgBefore, costAvgBefore);
    }
}
