using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// One unit a <see cref="Product"/> may be sold in - a <c>product_uom</c> row, carried with the
/// unit's own <c>decimal_places</c> and <c>symbol</c> so <see cref="UomConverter"/> and its
/// callers never need a second trip to the <c>uom</c> table (docs/01_DATA_MODEL.md §3, §8, SRS
/// FR-2.4, FR-2.5).
/// </summary>
/// <param name="UomId">The <c>uom.id</c> this option sells in.</param>
/// <param name="Symbol">The unit's display symbol, for example <c>"m"</c> or <c>"box"</c> - used only to word a plain-language message.</param>
/// <param name="DecimalPlaces">The unit's own <c>decimal_places</c> (0-4, <c>uom.decimal_places</c>).</param>
/// <param name="Conversion">How many base units one of this unit is worth.</param>
/// <param name="IsBase">True for the product's one base unit (<c>product_uom.is_base = 1</c>).</param>
/// <param name="SellingPrice">
/// The price for this unit, if the shop set one explicitly. Null means "base price × factor"
/// (FR-2.5) - resolved by <see cref="UomConverter.ResolvePrice"/>, never collapsed here.
/// </param>
public sealed record ProductUomOption(
    long UomId,
    string Symbol,
    int DecimalPlaces,
    UomConversion Conversion,
    bool IsBase,
    Money? SellingPrice);
