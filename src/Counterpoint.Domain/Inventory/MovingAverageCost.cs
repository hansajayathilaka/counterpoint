using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Inventory;

/// <summary>
/// The one formula behind the stock projection's moving-average cost column (P1-T07, SRS FR-4, DM-05).
/// </summary>
/// <remarks>
/// <para>
/// <c>newAvg = (oldQty×oldAvg + inQty×inCost) / (oldQty + inQty)</c>. Pure decimal arithmetic
/// over <see cref="Money"/> and <see cref="Quantity"/> - never <c>double</c> or <c>float</c>
/// (CLAUDE.md invariant 1) - and no I/O, so it is provable with a hand-worked example alone.
/// </para>
/// <para>
/// Only an inbound movement recomputes the average: a <c>GRN</c>, a sale return coming back in,
/// an opening count, a positive stock take or adjustment. An outbound movement never calls this -
/// it snapshots the cost already on the shelf instead (<c>Counterpoint.Domain.Inventory.StockLedgerMath</c>).
/// </para>
/// </remarks>
public static class MovingAverageCost
{
    /// <summary>
    /// Recomputes the moving-average cost after <paramref name="inQtyBase"/> of stock arrives at
    /// <paramref name="inUnitCost"/> each, on top of <paramref name="oldQtyBase"/> already on the
    /// shelf at <paramref name="oldAvgCost"/>.
    /// </summary>
    /// <param name="oldQtyBase">What was on the shelf before this movement, in base units.</param>
    /// <param name="oldAvgCost">The moving-average cost before this movement.</param>
    /// <param name="inQtyBase">What just arrived. Must be positive - this is the inbound case.</param>
    /// <param name="inUnitCost">What it cost, per base unit.</param>
    /// <returns>
    /// The new moving-average cost. Guarded against division by zero and a non-positive resulting
    /// quantity (P1-T07): when <c>oldQty + inQty</c> is not positive - a shop trading deep enough
    /// into negative stock (Q-11 allows it) that one receipt does not bring it back above zero -
    /// there is no meaningful weighted average of a non-positive prior balance, so the average
    /// simply becomes the cost of what just arrived.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="inQtyBase"/> is not positive.</exception>
    public static Money Recompute(Quantity oldQtyBase, Money oldAvgCost, Quantity inQtyBase, Money inUnitCost)
    {
        if (!inQtyBase.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inQtyBase),
                inQtyBase,
                "The moving-average cost only recomputes on an inbound (positive) movement. "
                + "An outbound movement snapshots the cost already on the shelf instead.");
        }

        var totalQty = oldQtyBase.Value + inQtyBase.Value;

        if (totalQty <= 0m)
        {
            return inUnitCost;
        }

        var totalCost = (oldAvgCost * oldQtyBase.Value) + (inUnitCost * inQtyBase.Value);
        return totalCost / totalQty;
    }
}
