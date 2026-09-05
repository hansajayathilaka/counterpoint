using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>sale_return_line</c> (docs/01_DATA_MODEL.md §6). APPEND ONLY.
/// See Schema/README.md.
/// </summary>
internal sealed class SaleReturnLine
{
    public long Id { get; set; }

    public long SaleReturnId { get; set; }

    /// <summary>Null when the return is unlinked.</summary>
    public long? SaleLineId { get; set; }

    public long ProductVariantId { get; set; }

    /// <summary>Quantity ×10 000, in base units. Always positive.</summary>
    public long QtyBase { get; set; }

    /// <summary>The price ORIGINALLY paid (AC-03), not today's price.</summary>
    public Money UnitPrice { get; set; }

    public Money UnitCost { get; set; }

    public Money Tax { get; set; }

    public Money LineRefund { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary><c>SELLABLE</c> or <c>DAMAGED</c>.</summary>
    public string Disposition { get; set; } = string.Empty;
}
