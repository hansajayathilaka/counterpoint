namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Which existing SKU a barcode already belongs to - what <see cref="IBarcodeStore.FindConflictAsync"/>
/// hands back so the hard-block message names the product a duplicate scan collided with
/// (SRS FR-2.24).
/// </summary>
public sealed record BarcodeConflict(
    long BarcodeId,
    long ProductVariantId,
    string Sku,
    string ProductName);
