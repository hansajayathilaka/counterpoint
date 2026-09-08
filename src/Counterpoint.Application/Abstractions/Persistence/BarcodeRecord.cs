namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>barcode</c> (docs/01_DATA_MODEL.md §3, SRS FR-2.9).</summary>
public sealed record BarcodeRecord(
    long Id,
    long ProductVariantId,
    string Value,
    bool IsPrimary);
