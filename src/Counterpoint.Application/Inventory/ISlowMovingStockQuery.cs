using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The slow-moving and dead stock report (task P2-T11 "Do this" #3; reworked by task P3-T06 "Do this"
/// #7, SRS §9 RPT-12 "items with no sale in N days, with value tied up").
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only since P3-T06.</b> Value tied up is <c>qty x cost_avg</c>, a cost figure, and SRS §9
/// lists RPT-12 for the owner role (CLAUDE.md invariant 8). Before P3-T06 this query carried no cost and
/// was open to any session.
/// </para>
/// <para>
/// <b>"No sale in N days", decided (P3-T06).</b> P2-T11 measured staleness from the last stock movement
/// of <i>any</i> type, so a goods receipt, a stock-take, an adjustment or a bulk break restarted a dead
/// line's clock. SRS RPT-12 says no <i>sale</i>, so a variant's idle time now starts at its most recent
/// <c>SALE</c> movement (a bill that was later cancelled does not count as a sale). A return, receipt or
/// count never resets it. A variant that has <i>never</i> sold is measured from its first ledger movement,
/// the day the stock first arrived, so a receipt from last week is not flagged as dead on day one, yet
/// stock that arrived long ago and never sold is. <see cref="SlowMovingStockLine.LastMovementAt"/> still
/// reports the last movement of any type, for information only.
/// </para>
/// <para>
/// One cutoff, not two hard-coded day counts: the caller draws the line. Every result has positive stock
/// on hand, so it necessarily has a ledger history (CLAUDE.md invariant 3).
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface ISlowMovingStockQuery
{
    /// <summary>
    /// Every active variant of an active product with positive stock on hand that has not sold since
    /// <paramref name="olderThan"/> (see the interface remarks), idlest first.
    /// </summary>
    public Task<IReadOnlyList<SlowMovingStockLine>> FindAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);

    /// <summary>The same, optionally narrowed to one category (and its children).</summary>
    public Task<IReadOnlyList<SlowMovingStockLine>> FindAsync(
        SlowMovingFilter filter,
        CancellationToken cancellationToken = default);
}

/// <summary>What the slow-moving report narrows to.</summary>
/// <param name="OlderThan">Variants whose last sale (or, never sold, first arrival) is on or before this instant.</param>
/// <param name="CategoryId">Only products filed under this category or one of its children; null is all.</param>
public sealed record SlowMovingFilter(DateTimeOffset OlderThan, long? CategoryId = null);

/// <summary>One variant that has not sold since the cutoff.</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="ProductDescription">The product's name.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="BaseUomSymbol">The product's base unit's display symbol.</param>
/// <param name="QtyOnHandBase">Current quantity on hand, in the base unit. Always positive.</param>
/// <param name="LastMovementAt">The most recent <c>stock_movement.occurred_at</c> of any type - information only.</param>
/// <param name="LastSaleAt">The most recent counted <c>SALE</c> movement, or null when the variant has never sold.</param>
/// <param name="IdleSince"><see cref="LastSaleAt"/>, or the first ledger movement when never sold: where the idle time is measured from.</param>
/// <param name="CategoryName">The product's category as filed now; empty when unfiled.</param>
/// <param name="CostAvg">The variant's current moving-average cost per base unit.</param>
/// <param name="ValueTiedUp"><see cref="QtyOnHandBase"/> times <see cref="CostAvg"/>, exact.</param>
public sealed record SlowMovingStockLine(
    long ProductVariantId,
    string ProductDescription,
    string Sku,
    string BaseUomSymbol,
    Quantity QtyOnHandBase,
    DateTimeOffset LastMovementAt,
    DateTimeOffset? LastSaleAt = null,
    DateTimeOffset IdleSince = default,
    string CategoryName = "",
    Money CostAvg = default,
    Money ValueTiedUp = default);
