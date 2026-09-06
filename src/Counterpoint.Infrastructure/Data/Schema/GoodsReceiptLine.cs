using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>goods_receipt_line</c> (docs/01_DATA_MODEL.md §4). See Schema/README.md.
/// </summary>
internal sealed class GoodsReceiptLine
{
    public long Id { get; set; }

    public long GoodsReceiptId { get; set; }

    public long ProductVariantId { get; set; }

    /// <summary>Quantity ×10 000, as entered, in <c>uom_id</c>.</summary>
    public long Qty { get; set; }

    public long UomId { get; set; }

    /// <summary>Quantity ×10 000, converted to base units (AC-08).</summary>
    public long QtyBase { get; set; }

    /// <summary>Per <c>uom_id</c>.</summary>
    public Money UnitCost { get; set; }

    /// <summary>Per base unit.</summary>
    public Money UnitCostBase { get; set; }

    public Money Tax { get; set; }

    public Money LineTotal { get; set; }
}
