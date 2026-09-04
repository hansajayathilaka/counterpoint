namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>product</c>. See Schema/README.md.</summary>
internal sealed class Product
{
    public long Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? NameAlt { get; set; }

    /// <summary>No foreign key yet: <c>category</c> arrives in P1-T01. See §13 of the data model.</summary>
    public long? CategoryId { get; set; }

    /// <summary>No foreign key yet: <c>brand</c> arrives in P1-T01. See §13 of the data model.</summary>
    public long? BrandId { get; set; }

    public long BaseUomId { get; set; }

    public string Type { get; set; } = string.Empty;

    public long TaxClassId { get; set; }

    /// <summary>Money ×10 000. Moving average, per base unit.</summary>
    public long CostAvg { get; set; }

    /// <summary>Quantity ×10 000.</summary>
    public long ReorderLevel { get; set; }

    /// <summary>Quantity ×10 000.</summary>
    public long ReorderQty { get; set; }

    public string? Location { get; set; }

    public bool NonReturnable { get; set; }

    /// <summary>Quantity ×10 000.</summary>
    public long MinSellQty { get; set; }

    /// <summary>Rate ×10 000. Null means "use the global limit".</summary>
    public long? MaxDiscountRate { get; set; }

    /// <summary>Plain count of days. Not scaled.</summary>
    public int? WarrantyDays { get; set; }

    public string? Notes { get; set; }

    public string? ImagePath { get; set; }

    public bool Active { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
