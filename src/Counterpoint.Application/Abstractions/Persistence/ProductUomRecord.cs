using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>product_uom</c> - a unit a product may be sold in (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.4, FR-2.5).</summary>
/// <param name="DecimalPlaces">
/// The unit's own <c>uom.decimal_places</c> - how many fractional digits a quantity sold in
/// <see cref="UomId"/> may carry (SRS FR-2.1-FR-2.8). Needed to build a
/// <c>Counterpoint.Domain.Catalogue.ProductUomOption</c> for <c>UomConverter</c>, which is why the
/// sale path (P1-T09) reads this record rather than <c>uom</c> a second time.
/// </param>
public sealed record ProductUomRecord(
    long Id,
    long ProductId,
    long UomId,
    string UomSymbol,
    UomConversion Conversion,
    Money? SellingPrice,
    bool IsBase,
    int DecimalPlaces);
