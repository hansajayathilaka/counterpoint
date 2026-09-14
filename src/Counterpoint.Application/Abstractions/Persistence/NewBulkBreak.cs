using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One bulk-break header to write onto <c>bulk_break</c> (docs/01_DATA_MODEL.md §4, SRS FR-4.9,
/// AC-09, task P2-T09).
/// </summary>
/// <param name="SourceVariantId">The variant being broken down (the coil, the box).</param>
/// <param name="DestinationVariantId">The variant it becomes (the loose metre, the loose piece).</param>
/// <param name="SourceQtyBase">
/// How much of the source left the shelf, in the source variant's own base unit - what
/// <c>BULK_BREAK_OUT</c> posts.
/// </param>
/// <param name="ExpectedQtyBase">
/// What the operator expected to get out, in the destination variant's own base unit, before
/// wastage was known.
/// </param>
/// <param name="ActualQtyBase">
/// What was actually counted out, in the destination variant's own base unit - what
/// <c>BULK_BREAK_IN</c> posts. <c>ExpectedQtyBase - ActualQtyBase</c> is the wastage.
/// </param>
/// <param name="WastageQtyBase">
/// <see cref="ExpectedQtyBase"/> minus <see cref="ActualQtyBase"/>, in the destination variant's
/// own base unit. Zero when nothing was lost - <c>PostBulkBreakHandler</c> posts no
/// <c>DAMAGE</c> row in that case.
/// </param>
/// <param name="TotalValue">
/// The source's own moving-average cost at the moment of the break, multiplied by
/// <see cref="SourceQtyBase"/> - what the <c>BULK_BREAK_OUT</c> movement carries out, and what
/// <c>BULK_BREAK_IN</c> plus the wastage <c>DAMAGE</c> (if any) sharing this row's id are built to
/// sum back to (SRS FR-4.9's "carrying cost across").
/// </param>
/// <param name="Reason">Why - mandatory, the same discipline every other manual stock event in this codebase carries.</param>
/// <param name="UserId">Who posted it.</param>
/// <param name="OccurredAt">When.</param>
public sealed record NewBulkBreak(
    long SourceVariantId,
    long DestinationVariantId,
    Quantity SourceQtyBase,
    Quantity ExpectedQtyBase,
    Quantity ActualQtyBase,
    Quantity WastageQtyBase,
    Money TotalValue,
    string Reason,
    long UserId,
    DateTimeOffset OccurredAt);
