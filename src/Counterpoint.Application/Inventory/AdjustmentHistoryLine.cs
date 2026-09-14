using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>One row of <see cref="IAdjustmentHistoryQuery"/> (task P2-T08 "Do this" #4).</summary>
/// <param name="MovementId">The <c>stock_movement.id</c> this line reads back.</param>
/// <param name="OccurredAt">When it was posted.</param>
/// <param name="MovementType">
/// <c>ADJUSTMENT</c> or <c>DAMAGE</c> (<see cref="Counterpoint.Domain.Inventory.AdjustmentTypes"/>).
/// </param>
/// <param name="ProductVariantId">The variant that moved.</param>
/// <param name="Sku">The variant's own SKU.</param>
/// <param name="Description">The product's name, as sold.</param>
/// <param name="QtyBase">The signed quantity this movement posted, in base units.</param>
/// <param name="BalanceAfter">The running balance immediately after this movement.</param>
/// <param name="UnitCost">What this movement was valued at - owner-only (CLAUDE.md invariant 8).</param>
/// <param name="Reason">The mandatory reason given when the movement was posted.</param>
/// <param name="UserId">Who posted it.</param>
/// <param name="UserDisplayName">Their name, for the report.</param>
public sealed record AdjustmentHistoryLine(
    long MovementId,
    DateTimeOffset OccurredAt,
    string MovementType,
    long ProductVariantId,
    string Sku,
    string Description,
    Quantity QtyBase,
    Quantity BalanceAfter,
    Money UnitCost,
    string? Reason,
    long UserId,
    string UserDisplayName);
