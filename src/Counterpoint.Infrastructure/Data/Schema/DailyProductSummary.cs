using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>daily_product_summary</c> (docs/01_DATA_MODEL.md §7). Keyed on
/// (business date, variant), not on an id. See Schema/README.md.
/// </summary>
internal sealed class DailyProductSummary
{
    /// <summary><c>YYYY-MM-DD</c> TEXT, not a timestamp. Half the primary key.</summary>
    public string BusinessDate { get; set; } = string.Empty;

    public long ProductVariantId { get; set; }

    /// <summary>Quantity ×10 000, in base units.</summary>
    public long QtyBase { get; set; }

    public Money Net { get; set; }

    public Money Cogs { get; set; }
}
