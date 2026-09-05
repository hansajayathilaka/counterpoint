using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>product</c>. See Schema/README.md.</summary>
internal sealed class Product
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? NameAlt { get; set; }

    /// <summary>Null for an unclassified product. Foreign key to <c>category</c> since P1-T01.</summary>
    public long? CategoryId { get; set; }

    /// <summary>Null for an unbranded product. Foreign key to <c>brand</c> since P1-T01.</summary>
    public long? BrandId { get; set; }

    public long BaseUomId { get; set; }

    public string Type { get; set; } = string.Empty;

    public long TaxClassId { get; set; }

    /// <summary>Moving average cost, per base unit.</summary>
    public Money CostAvg { get; set; }

    /// <summary>Quantity ×10 000.</summary>
    public long ReorderLevel { get; set; }

    /// <summary>Quantity ×10 000.</summary>
    public long ReorderQty { get; set; }

    public string? Location { get; set; }

    public bool NonReturnable { get; set; }

    /// <summary>Quantity ×10 000.</summary>
    public long MinSellQty { get; set; }

    /// <summary>Null means "use the global limit".</summary>
    public Percentage? MaxDiscountRate { get; set; }

    /// <summary>Plain count of days. Not scaled.</summary>
    public int? WarrantyDays { get; set; }

    public string? Notes { get; set; }

    public string? ImagePath { get; set; }

    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
