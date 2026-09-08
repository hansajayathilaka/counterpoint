using System;
using System.Globalization;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// Converts a quantity between a product's units and its base unit, and resolves what a unit
/// sells for (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.1-FR-2.8, FR-3.6, AC-08).
/// </summary>
/// <remarks>
/// <para>
/// Stock is always held in base units; every sale, receipt and report converts to base units at
/// the boundary and never carries mixed units inward (SRS §2.2). This is that boundary.
/// </para>
/// <para>
/// Every step is exact <see cref="decimal"/> arithmetic, so
/// <c>FromBase(ToBase(q, uomId, product), uomId, product) == q</c> for every quantity the storage
/// scale can represent - there is no floating-point rounding for a round trip to drift on.
/// </para>
/// </remarks>
public static class UomConverter
{
    /// <summary>
    /// Converts <paramref name="qty"/>, entered in <paramref name="uomId"/>, into the product's
    /// base unit - the number a stock movement or a <c>sale_line.qty_base</c> would use.
    /// </summary>
    /// <param name="qty">The quantity as entered, in <paramref name="uomId"/>.</param>
    /// <param name="uomId">The unit <paramref name="qty"/> was entered in.</param>
    /// <param name="product">The product being sold.</param>
    /// <exception cref="InvalidOperationException">
    /// The product does not sell in <paramref name="uomId"/>, or <paramref name="qty"/> is not a
    /// quantity <paramref name="product"/>'s type allows in that unit (FR-2.1-FR-2.8): a
    /// <see cref="ProductType.Standard"/> product rejects any fractional part regardless of the
    /// unit's own decimal places, and every other type rejects more fractional digits than the
    /// unit's <c>decimal_places</c> allows.
    /// </exception>
    public static Quantity ToBase(decimal qty, long uomId, Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var option = product.RequireUom(uomId);
        RequireStorableQuantity(qty, product, option);

        var baseQty = qty * option.Conversion.Factor;
        return Quantity.FromDecimal(baseQty, product.BaseUomId);
    }

    /// <summary>
    /// Converts <paramref name="qtyBase"/>, a quantity already in the product's base unit, into
    /// <paramref name="uomId"/> - the reverse of <see cref="ToBase"/>, for display or reprinting
    /// a quantity in the unit it was sold in.
    /// </summary>
    /// <param name="qtyBase">The quantity in the product's base unit.</param>
    /// <param name="uomId">The unit to express it in.</param>
    /// <param name="product">The product.</param>
    /// <exception cref="InvalidOperationException">The product does not sell in <paramref name="uomId"/>.</exception>
    public static Quantity FromBase(decimal qtyBase, long uomId, Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var option = product.RequireUom(uomId);
        var qty = qtyBase / option.Conversion.Factor;
        return Quantity.FromDecimal(qty, uomId);
    }

    /// <summary>
    /// What one of <paramref name="uomId"/> sells for: <c>product_uom.selling_price</c> if the
    /// shop set one, otherwise the variant's base-unit price times the unit's conversion factor
    /// (FR-2.5). A distinct price per unit is the point - never collapsed to the base price.
    /// </summary>
    /// <param name="variantBasePrice"><c>product_variant.price</c>, per base unit.</param>
    /// <param name="uomId">The unit to price.</param>
    /// <param name="product">The product.</param>
    /// <exception cref="InvalidOperationException">The product does not sell in <paramref name="uomId"/>.</exception>
    public static Money ResolvePrice(Money variantBasePrice, long uomId, Product product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var option = product.RequireUom(uomId);
        return option.SellingPrice ?? variantBasePrice.Multiply(option.Conversion.Factor);
    }

    private static void RequireStorableQuantity(decimal qty, Product product, ProductUomOption option)
    {
        if (product.Type == ProductType.Standard)
        {
            if (qty != Math.Truncate(qty))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"'{product.Name}' is sold in whole {option.Symbol} units only. {qty} {option.Symbol} is not a whole number."));
            }

            return;
        }

        var rounded = decimal.Round(qty, option.DecimalPlaces, MidpointRounding.AwayFromZero);

        if (rounded != qty)
        {
            var placeWord = option.DecimalPlaces == 1 ? "decimal place" : "decimal places";

            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"'{product.Name}' is sold in {option.Symbol} to at most {option.DecimalPlaces} {placeWord}. {qty} {option.Symbol} has more than that."));
        }
    }
}
