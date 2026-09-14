using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// What one adjustment or damage write-off did (task P2-T08 "Done when": the correct sign, cost
/// and balance after).
/// </summary>
/// <param name="ProductVariantId">The variant that moved.</param>
/// <param name="MovementType">
/// The <c>stock_movement.movement_type</c> token posted - <c>ADJUSTMENT</c> or <c>DAMAGE</c>
/// (<see cref="Counterpoint.Domain.Inventory.AdjustmentTypes"/>).
/// </param>
/// <param name="Delta">The signed quantity actually posted, in the variant's own base unit.</param>
/// <param name="QtyBefore">The balance immediately before this movement.</param>
/// <param name="QtyAfter">The balance immediately after - <c>stock_movement.balance_after</c>.</param>
/// <param name="UnitCost">
/// The moving-average cost this movement was valued at - the same cost recorded on the movement
/// row, read fresh at the moment of posting (task P2-T08 "Do this" #2).
/// </param>
/// <param name="Warning">
/// Set when an inbound adjustment's value exceeds
/// <see cref="Counterpoint.Application.Settings.PolicySettings.AdjustmentGrnWarningThreshold"/> -
/// a nudge towards a goods receipt instead, never a refusal (task P2-T08's own "Risks", read
/// through CLAUDE.md invariant 7's "never block" spirit - here applied to a policy nudge rather
/// than a device failure). Null otherwise.
/// </param>
public sealed record AdjustmentResult(
    long ProductVariantId,
    string MovementType,
    Quantity Delta,
    Quantity QtyBefore,
    Quantity QtyAfter,
    Money UnitCost,
    string? Warning);
