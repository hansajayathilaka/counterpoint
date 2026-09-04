namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>stock_balance</c>: the projection of the <c>stock_movement</c> ledger
/// (CLAUDE.md invariant 3). The primary key is the foreign key. See Schema/README.md.
/// </summary>
internal sealed class StockBalance
{
    public long ProductVariantId { get; set; }

    /// <summary>Quantity ×10 000, in base units.</summary>
    public long QtyBase { get; set; }

    /// <summary>Money ×10 000.</summary>
    public long CostAvg { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
