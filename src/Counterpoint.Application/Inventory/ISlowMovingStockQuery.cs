using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The slow-moving and non-moving stock report (task P2-T11 "Do this" #3, SRS FR-4 reorder):
/// every variant currently holding stock whose most recent ledger movement was on or before a
/// caller-supplied cutoff.
/// </summary>
/// <remarks>
/// <para>
/// Not owner-only - a quantity and a date carry no cost or margin figure (CLAUDE.md invariant 8),
/// the same reasoning <see cref="IReorderListQuery"/> draws.
/// </para>
/// <para>
/// One cutoff, not two hard-coded "slow" and "non-moving" day counts: task P2-T11 cites no
/// SRS-mandated number of days for either bucket, and inventing one here would be a business rule
/// this task was not asked to set. The caller draws the line - a later report screen can call this
/// twice, once for each of its own configurable thresholds, without this query's shape changing.
/// </para>
/// <para>
/// Every result already has at least one ledger movement behind it: reading from
/// <c>stock_balance.qty_base &gt; 0</c> means a movement necessarily posted that balance (CLAUDE.md
/// invariant 3 - the projection is never written any other way), so
/// <see cref="SlowMovingStockLine.LastMovementAt"/> is never null. A variant that has never
/// received a single movement therefore also never has stock, and has nothing this report needs
/// to flag.
/// </para>
/// </remarks>
public interface ISlowMovingStockQuery
{
    /// <summary>
    /// Every active variant of an active product with positive stock on hand whose most recent
    /// <c>stock_movement.occurred_at</c> is on or before <paramref name="olderThan"/>, oldest
    /// first.
    /// </summary>
    public Task<IReadOnlyList<SlowMovingStockLine>> FindAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}

/// <summary>One variant that has not moved since <c>olderThan</c> (task P2-T11 "Do this" #3).</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="ProductDescription">The product's name.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="BaseUomSymbol">The product's base unit's display symbol.</param>
/// <param name="QtyOnHandBase">Current quantity on hand, in the base unit. Always positive - see the interface's own remarks.</param>
/// <param name="LastMovementAt">The most recent <c>stock_movement.occurred_at</c> for this variant.</param>
public sealed record SlowMovingStockLine(
    long ProductVariantId,
    string ProductDescription,
    string Sku,
    string BaseUomSymbol,
    Quantity QtyOnHandBase,
    DateTimeOffset LastMovementAt);
