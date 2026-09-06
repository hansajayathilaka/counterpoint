namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>
/// Row of <c>barcode</c> (docs/01_DATA_MODEL.md §3). The unique index on <c>barcode</c> is the
/// hottest lookup in the system (NFR-P1). See Schema/README.md.
/// </summary>
internal sealed class Barcode
{
    public long Id { get; set; }

    public long ProductVariantId { get; set; }

    /// <summary>The scanned symbol, as printed on the packet.</summary>
    public string Value { get; set; } = string.Empty;

    public bool IsPrimary { get; set; }
}
