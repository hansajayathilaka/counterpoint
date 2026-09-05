using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>product_supplier</c> (docs/01_DATA_MODEL.md §3). See Schema/README.md.
/// </summary>
internal sealed class ProductSupplier
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public long SupplierId { get; set; }

    /// <summary>The supplier's own code for this line, as it appears on their invoice.</summary>
    public string? SupplierRef { get; set; }

    public Money? LastCost { get; set; }
}
