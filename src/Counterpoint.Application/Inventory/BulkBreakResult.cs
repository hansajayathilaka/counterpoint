using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>What one bulk break posted (task P2-T09 "Done when": a balanced pair with cost carried across, and matching wastage).</summary>
/// <param name="BulkBreakId">The <c>bulk_break.id</c> - the <c>ref_doc_id</c> every movement this break posted shares.</param>
/// <param name="SourceVariantId">The variant broken down.</param>
/// <param name="DestinationVariantId">The variant it became.</param>
/// <param name="SourceQtyBase">What left the source, in the source variant's own base unit - what <c>BULK_BREAK_OUT</c> posted.</param>
/// <param name="SourceCostAvg">
/// The source's own moving-average cost at the moment of the break - what <c>BULK_BREAK_OUT</c>
/// (and the wastage <c>DAMAGE</c>, if any) were valued at. Never invented (CLAUDE.md invariant 3's
/// "never SUM the ledger" spirit, applied here to cost: the value already on the shelf, read once).
/// </param>
/// <param name="TotalValue"><see cref="SourceCostAvg"/> × <see cref="SourceQtyBase"/> - the value carried out of the source.</param>
/// <param name="ExpectedQtyBase">What the break was expected to yield, in the destination's own base unit.</param>
/// <param name="ActualQtyBase">What was actually counted out - what <c>BULK_BREAK_IN</c> posted.</param>
/// <param name="DestinationUnitCost">
/// The cost per base unit <c>BULK_BREAK_IN</c> recomputed the destination's moving average
/// against - <see cref="TotalValue"/> plus <see cref="WastageValue"/>, spread across
/// <see cref="ActualQtyBase"/>, so the surviving units carry the full cost of the batch that was
/// broken, wastage included (the same absorption every process-costing write-off uses, so no
/// value quietly vanishes off the books).
/// </param>
/// <param name="WastageQtyBase"><see cref="ExpectedQtyBase"/> minus <see cref="ActualQtyBase"/>. Zero when nothing was lost.</param>
/// <param name="WastageValue">
/// What the wastage <c>DAMAGE</c> movement wrote off, or null when <see cref="WastageQtyBase"/> is
/// zero and no <c>DAMAGE</c> row was posted at all.
/// </param>
public sealed record BulkBreakResult(
    long BulkBreakId,
    long SourceVariantId,
    long DestinationVariantId,
    Quantity SourceQtyBase,
    Money SourceCostAvg,
    Money TotalValue,
    Quantity ExpectedQtyBase,
    Quantity ActualQtyBase,
    Money DestinationUnitCost,
    Quantity WastageQtyBase,
    Money? WastageValue);
