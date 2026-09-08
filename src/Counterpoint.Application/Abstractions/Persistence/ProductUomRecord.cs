using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>product_uom</c> - a unit a product may be sold in (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.4, FR-2.5).</summary>
public sealed record ProductUomRecord(
    long Id,
    long ProductId,
    long UomId,
    string UomSymbol,
    UomConversion Conversion,
    Money? SellingPrice,
    bool IsBase);
