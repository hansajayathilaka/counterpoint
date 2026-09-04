namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>product_variant</c>. See Schema/README.md.</summary>
internal sealed class ProductVariant
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public string Sku { get; set; } = string.Empty;

    /// <summary>JSON, for example <c>{"size":"M8"}</c>.</summary>
    public string Attributes { get; set; } = "{}";

    /// <summary>Money ×10 000, per base unit, retail.</summary>
    public long Price { get; set; }

    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
