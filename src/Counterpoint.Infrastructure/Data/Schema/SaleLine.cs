using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>sale_line</c>. APPEND ONLY apart from <c>qty_returned</c>. Snapshots
/// <c>description</c>, <c>unit_price</c> and <c>unit_cost</c> (CLAUDE.md invariant 10).
/// See Schema/README.md.
/// </summary>
internal sealed class SaleLine
{
    public long Id { get; set; }

    public long SaleId { get; set; }

    /// <summary>Plain 1-based ordinal within the bill. Not scaled.</summary>
    public int LineNo { get; set; }

    /// <summary>Null for an open item.</summary>
    public long? ProductVariantId { get; set; }

    /// <summary>Snapshot: the name as sold.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Quantity ×10 000, in <c>uom_id</c>.</summary>
    public long Qty { get; set; }

    public long UomId { get; set; }

    /// <summary>Quantity ×10 000, in base units.</summary>
    public long QtyBase { get; set; }

    /// <summary>Per <c>uom_id</c>, as charged.</summary>
    public Money UnitPrice { get; set; }

    public Money Discount { get; set; }

    /// <summary>The rate charged on this line, snapshotted.</summary>
    public TaxRate TaxRate { get; set; }

    public Money Tax { get; set; }

    public Money LineTotal { get; set; }

    /// <summary>COGS snapshot, per base unit.</summary>
    public Money UnitCost { get; set; }

    /// <summary>Quantity ×10 000, in base units (AC-06). The one column that may be updated.</summary>
    public long QtyReturned { get; set; }

    public string? Note { get; set; }
}
