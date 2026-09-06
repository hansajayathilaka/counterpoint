using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>purchase_order_line</c> (docs/01_DATA_MODEL.md §4). See Schema/README.md.
/// </summary>
internal sealed class PurchaseOrderLine
{
    public long Id { get; set; }

    public long PurchaseOrderId { get; set; }

    public long ProductVariantId { get; set; }

    /// <summary>Quantity ×10 000, in <c>uom_id</c> - not in base units.</summary>
    public long Qty { get; set; }

    public long UomId { get; set; }

    /// <summary>Per <c>uom_id</c>.</summary>
    public Money UnitCost { get; set; }

    /// <summary>Quantity ×10 000, in base units. Raised as goods receipts land against the order.</summary>
    public long QtyReceivedBase { get; set; }
}
