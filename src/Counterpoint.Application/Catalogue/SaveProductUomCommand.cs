using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// What <see cref="IProductMaintenance"/> needs to add or edit one of a product's non-base units
/// (SRS FR-2.4, FR-2.5).
/// </summary>
/// <param name="UomId">An existing, active unit - not the product's base unit.</param>
/// <param name="Conversion">How many base units one of this unit is worth, for example 100 for "1 box = 100 pieces".</param>
/// <param name="SellingPrice">A price for this unit, or null for "base price x factor" (FR-2.5).</param>
public sealed record SaveProductUomCommand(long UomId, UomConversion Conversion, Money? SellingPrice);
