namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>supplier</c> (docs/01_DATA_MODEL.md §4). See Schema/README.md.</summary>
internal sealed class Supplier
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Contact { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public string? TaxNo { get; set; }

    public string? PaymentTerms { get; set; }

    public bool Active { get; set; }
}
