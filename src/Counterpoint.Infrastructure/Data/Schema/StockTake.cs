namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>stock_take</c> (docs/01_DATA_MODEL.md §4). See Schema/README.md.</summary>
internal sealed class StockTake
{
    public long Id { get; set; }

    /// <summary><c>ALL</c>, <c>CATEGORY:12</c>, <c>BRAND:5</c>, <c>LOCATION:A3</c>.</summary>
    public string Scope { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public long UserId { get; set; }

    /// <summary>
    /// Allocated from <c>number_sequence</c> (<c>doc_type = 'STOCK_TAKE'</c>) when the count sheet
    /// is generated, the same way <see cref="GoodsReceipt.GrnNo"/> and
    /// <see cref="PurchaseOrder.PoNo"/> are - printed on the count sheet (FR-7.10) and how a
    /// reprint or a resumed count session finds the right sheet. Declared last: added by
    /// <c>StockTakeNumber0008</c> as a plain <c>ADD COLUMN</c>, which SQLite appends physically
    /// (docs/01_DATA_MODEL.md §13, the same as <c>Uom.Active</c>).
    /// </summary>
    public string StockTakeNo { get; set; } = string.Empty;
}
