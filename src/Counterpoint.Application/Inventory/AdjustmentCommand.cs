using System;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// One manual stock correction (SRS FR-4, task P2-T08 "Do this" #1).
/// </summary>
/// <remarks>
/// Exactly one of <see cref="QuantityDelta"/> and <see cref="TargetQuantity"/> must be given - the
/// screen's own two entry modes ("a signed quantity or a target quantity"), never both and never
/// neither: <c>PostAdjustmentHandler</c> refuses a command carrying zero or two of them before it
/// ever opens a transaction. <see cref="TargetQuantity"/> is resolved against the variant's own
/// current balance inside the very transaction that posts the movement, not against a value read
/// earlier in the request, so two adjustments started moments apart can never both compute their
/// delta against the same stale balance.
/// </remarks>
/// <param name="ProductVariantId">The variant being corrected.</param>
/// <param name="QuantityDelta">
/// A signed change, in the variant's own base unit: positive is found stock, negative is lost
/// stock. Null when <see cref="TargetQuantity"/> is given instead.
/// </param>
/// <param name="TargetQuantity">
/// What the shelf should read after this adjustment, in the variant's own base unit. Null when
/// <see cref="QuantityDelta"/> is given instead.
/// </param>
/// <param name="Reason">
/// Why the count moved - mandatory (task P2-T08's own "Owner only, reason mandatory, fully
/// audited"), the same "a blank reason is refused" discipline
/// <see cref="Counterpoint.Application.Returns.CreateUnlinkedReturnCommand.Reason"/> carries. Built
/// by the caller from a configurable reason list, free text, or both
/// (<see cref="Counterpoint.Application.Settings.PolicySettings.AdjustmentReasons"/>) - this door
/// only requires that the text that reaches it is not blank.
/// </param>
/// <param name="OccurredAt">When the count was taken. Null uses now.</param>
public sealed record AdjustmentCommand(
    long ProductVariantId,
    decimal? QuantityDelta,
    decimal? TargetQuantity,
    string Reason,
    DateTimeOffset? OccurredAt = null);
