using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>product_uom</c> (docs/01_DATA_MODEL.md §3): the units a product may be sold in, and
/// what each is worth in base units. See Schema/README.md.
/// </summary>
internal sealed class ProductUom
{
    public long Id { get; set; }

    public long ProductId { get; set; }

    public long UomId { get; set; }

    /// <summary>
    /// Scaled ×10 000, base units per one of this unit: 1 box = 100 pieces is <c>1000000</c>.
    /// Not a <c>Quantity</c> - it is a ratio between two units, not an amount of one.
    /// </summary>
    public long ConversionFactor { get; set; }

    /// <summary>Null means "base price × factor" (FR-2.5).</summary>
    public Money? SellingPrice { get; set; }

    public bool IsBase { get; set; }
}
