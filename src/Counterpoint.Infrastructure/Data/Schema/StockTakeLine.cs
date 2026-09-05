namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>stock_take_line</c> (docs/01_DATA_MODEL.md §4). See Schema/README.md.
/// </summary>
internal sealed class StockTakeLine
{
    public long Id { get; set; }

    public long StockTakeId { get; set; }

    public long ProductVariantId { get; set; }

    /// <summary>Quantity ×10 000. Frozen when the count sheet is generated.</summary>
    public long SystemQty { get; set; }

    /// <summary>Quantity ×10 000. Null until the shelf is counted.</summary>
    public long? CountedQty { get; set; }

    /// <summary>Quantity ×10 000, counted minus system.</summary>
    public long? Variance { get; set; }

    public DateTimeOffset? CountedAt { get; set; }
}
