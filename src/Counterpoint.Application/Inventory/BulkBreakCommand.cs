using System;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// One request to convert stock from one packaging form into another (SRS FR-4.9, AC-09, task
/// P2-T09) - a coil into loose metres, a box into loose pieces.
/// </summary>
/// <remarks>
/// <see cref="ExpectedQuantity"/> and <see cref="ActualQuantity"/> are both given, the same
/// two-numbers-in shape <c>bulk_break</c>'s own <c>expected_qty_base</c>/<c>actual_qty_base</c>
/// columns carry (docs/01_DATA_MODEL.md §4): the operator states what the break was expected to
/// yield and what it actually yielded once counted, and <c>PostBulkBreakHandler</c> derives the
/// wastage as the difference - never the other way around (a declared wastage figure with the
/// handler back-solving "actual"), because what physically lands on the destination's shelf is the
/// number that must match what <c>BULK_BREAK_IN</c> posts, and that is the operator's own count,
/// not an arithmetic inference from a separately-typed wastage estimate.
/// </remarks>
/// <param name="SourceVariantId">The variant being broken down.</param>
/// <param name="SourceQuantity">
/// How much of the source is being broken, in the source variant's own base unit. Always positive.
/// </param>
/// <param name="DestinationVariantId">
/// The variant it becomes. Must differ from <see cref="SourceVariantId"/> - a bulk break moves
/// stock between two different SKUs, never the same one (FR-4.9's own distinction from a plain
/// UOM conversion, <c>ck_bulk_break_distinct_variants</c>).
/// </param>
/// <param name="ExpectedQuantity">
/// What the break was expected to yield, in the destination variant's own base unit, before
/// wastage was known. Always positive.
/// </param>
/// <param name="ActualQuantity">
/// What was actually counted out, in the destination variant's own base unit - what
/// <c>BULK_BREAK_IN</c> posts. Always positive, and never more than <see cref="ExpectedQuantity"/>
/// (a break cannot yield more than it was expected to).
/// </param>
/// <param name="Reason">
/// Why - mandatory, the same discipline every other manual stock event in this codebase carries
/// (<see cref="Counterpoint.Application.Inventory.AdjustmentCommand.Reason"/>).
/// </param>
/// <param name="OccurredAt">When the break was done. Null uses now.</param>
public sealed record BulkBreakCommand(
    long SourceVariantId,
    decimal SourceQuantity,
    long DestinationVariantId,
    decimal ExpectedQuantity,
    decimal ActualQuantity,
    string Reason,
    DateTimeOffset? OccurredAt = null);
