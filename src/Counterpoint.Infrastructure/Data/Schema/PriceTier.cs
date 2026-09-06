using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>price_tier</c> (docs/01_DATA_MODEL.md §3). See Schema/README.md.</summary>
internal sealed class PriceTier
{
    public long Id { get; set; }

    public long ProductVariantId { get; set; }

    /// <summary><c>RETAIL</c> or <c>TRADE</c>.</summary>
    public string Tier { get; set; } = string.Empty;

    /// <summary>Quantity ×10 000, in base units. The break this price starts at.</summary>
    public long MinQty { get; set; }

    public Money Price { get; set; }

    /// <summary><c>YYYY-MM-DD</c> TEXT, not a timestamp. Null means "always been in force".</summary>
    public string? ValidFrom { get; set; }

    /// <summary><c>YYYY-MM-DD</c> TEXT, not a timestamp. Null means "no end date".</summary>
    public string? ValidTo { get; set; }
}
