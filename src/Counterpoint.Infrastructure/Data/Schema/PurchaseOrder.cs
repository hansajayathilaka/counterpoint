namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>purchase_order</c> (docs/01_DATA_MODEL.md §4). <c>po_no</c> comes from
/// <c>number_sequence</c>, never from the rowid (CLAUDE.md invariant 4). See Schema/README.md.
/// </summary>
internal sealed class PurchaseOrder
{
    public long Id { get; set; }

    public string PoNo { get; set; } = string.Empty;

    public long SupplierId { get; set; }

    public DateTimeOffset OrderedAt { get; set; }

    public DateTimeOffset? ExpectedAt { get; set; }

    public string Status { get; set; } = string.Empty;

    public long UserId { get; set; }

    public string? Note { get; set; }
}
