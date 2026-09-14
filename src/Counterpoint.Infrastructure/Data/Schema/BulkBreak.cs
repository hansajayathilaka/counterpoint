using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>bulk_break</c> (docs/01_DATA_MODEL.md §4, task P2-T09). Not append-only — the same
/// mutability as <c>goods_receipt</c>, <c>purchase_order</c> and <c>stock_take</c>, none of which
/// are on CLAUDE.md invariant 5's list.
/// </summary>
/// <remarks>
/// The one job this row does that no CHECK constraint could: hand out an id neither the
/// <c>BULK_BREAK_OUT</c>, <c>BULK_BREAK_IN</c> nor the wastage <c>DAMAGE</c>
/// <c>stock_movement</c> row could mint for itself, so all three can carry the identical,
/// pre-known <c>ref_doc_id</c> the task's "balanced pair" (and the wastage row beside it) needs —
/// <c>stock_movement</c> is append-only, so a row already written can never be updated with its
/// own id after the fact, and <c>ref_doc_id</c> carries no foreign key of its own for this to lean
/// on (unlike <c>sale.id</c>, which <c>CancelSaleHandler</c>'s own compensating movements already
/// reuse, because that header row exists before the transaction that reverses it ever opens).
/// See Schema/README.md.
/// </remarks>
internal sealed class BulkBreak
{
    public long Id { get; set; }

    public long SourceVariantId { get; set; }

    public long DestinationVariantId { get; set; }

    /// <summary>Quantity ×10 000, in the source variant's base unit.</summary>
    public long SourceQtyBase { get; set; }

    /// <summary>Quantity ×10 000, in the destination variant's base unit — what the screen asked
    /// for before wastage was known to have eaten into it.</summary>
    public long ExpectedQtyBase { get; set; }

    /// <summary>Quantity ×10 000, in the destination variant's base unit — what the
    /// <c>BULK_BREAK_IN</c> movement actually posted.</summary>
    public long ActualQtyBase { get; set; }

    /// <summary>Quantity ×10 000, in the destination variant's base unit —
    /// <c>ExpectedQtyBase - ActualQtyBase</c>, and what the wastage <c>DAMAGE</c> movement, if
    /// any, was valued against. Zero when nothing was lost.</summary>
    public long WastageQtyBase { get; set; }

    /// <summary>The total value carried across from source to destination:
    /// the source's <c>cost_avg</c> at the moment of the break, multiplied by
    /// <see cref="SourceQtyBase"/>. What the value-conservation report expects the
    /// <c>BULK_BREAK_OUT</c>, <c>BULK_BREAK_IN</c> and wastage <c>DAMAGE</c> rows sharing this
    /// row's id to sum to zero against.</summary>
    public Money TotalValue { get; set; }

    public string Reason { get; set; } = string.Empty;

    public long UserId { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
