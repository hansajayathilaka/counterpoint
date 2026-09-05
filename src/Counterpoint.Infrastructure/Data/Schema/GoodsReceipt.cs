using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>goods_receipt</c> (docs/01_DATA_MODEL.md §4). See Schema/README.md.
/// </summary>
internal sealed class GoodsReceipt
{
    public long Id { get; set; }

    public string GrnNo { get; set; } = string.Empty;

    public long SupplierId { get; set; }

    /// <summary>Null for a receipt that was never ordered on paper.</summary>
    public long? PurchaseOrderId { get; set; }

    public string? SupplierInvNo { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public Money Subtotal { get; set; }

    public Money Tax { get; set; }

    /// <summary>Freight and the like, apportioned across the lines.</summary>
    public Money OtherCost { get; set; }

    public Money Total { get; set; }

    public long UserId { get; set; }

    public string? Note { get; set; }
}
