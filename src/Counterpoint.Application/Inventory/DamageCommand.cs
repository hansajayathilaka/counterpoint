using System;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// One damage write-off (SRS FR-4, task P2-T08 "Do this" #2) - always a stock decrease, so unlike
/// <see cref="AdjustmentCommand"/> there is no signed-or-target choice to make: the operator
/// states how many units are being written off, never which direction.
/// </summary>
/// <param name="ProductVariantId">The variant being written off.</param>
/// <param name="Quantity">
/// How many units were damaged, in the variant's own base unit. Always positive -
/// <c>PostAdjustmentHandler</c> is the one place that turns it into the negative movement.
/// </param>
/// <param name="Reason">
/// Why - mandatory, the same discipline <see cref="AdjustmentCommand.Reason"/> carries.
/// </param>
/// <param name="OccurredAt">When the damage was found. Null uses now.</param>
public sealed record DamageCommand(
    long ProductVariantId,
    decimal Quantity,
    string Reason,
    DateTimeOffset? OccurredAt = null);
