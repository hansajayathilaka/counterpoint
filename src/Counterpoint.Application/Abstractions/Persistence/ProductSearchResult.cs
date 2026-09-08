using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One row of a counter search (SRS FR-2.11, NFR-P2). A cashier DTO, the same as
/// <c>Counterpoint.Application.Sales.ScannedItem</c>: no cost field, because cost never reaches a
/// cashier's screen (CLAUDE.md invariant 8, AC-17).
/// </summary>
/// <param name="ProductVariantId">What scanning or picking this result adds to the bill.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="ProductName">The product name, as the bill line will show it.</param>
/// <param name="BrandName">The brand, or null when the product has none.</param>
/// <param name="CategoryName">The category, or null when the product is unclassified.</param>
/// <param name="Location">The rack or bin, or null.</param>
/// <param name="BaseUomId">The product's base unit - what <see cref="QtyOnHand"/> is measured in.</param>
/// <param name="UomSymbol">The base unit's symbol.</param>
/// <param name="UnitPrice">Retail price per base unit.</param>
/// <param name="QtyOnHand">The stock balance projection at the moment of the search, in base units.</param>
/// <param name="IsExactCodeMatch">
/// True when the search text matched the product code or the variant SKU exactly, not merely a
/// term inside one of the indexed columns - what ranks a result first (P1-T06 "ranked with exact
/// code matches first").
/// </param>
public sealed record ProductSearchResult(
    long ProductVariantId,
    string Sku,
    string ProductName,
    string? BrandName,
    string? CategoryName,
    string? Location,
    long BaseUomId,
    string UomSymbol,
    Money UnitPrice,
    Quantity QtyOnHand,
    bool IsExactCodeMatch);
