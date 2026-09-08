using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// What a <see cref="ProductType"/> is called in the database, and back
/// (docs/01_DATA_MODEL.md §3). The same shape as
/// <c>Counterpoint.Domain.Security.Roles</c>: the four tokens are exactly the ones
/// <c>ck_product_type</c> constrains <c>product.type</c> to, spelled out once so no adapter can
/// spell them differently.
/// </summary>
public static class ProductTypes
{
    /// <summary>The <c>product.type</c> value for <see cref="ProductType.Standard"/>.</summary>
    public const string StandardToken = "STANDARD";

    /// <summary>The <c>product.type</c> value for <see cref="ProductType.Fractional"/>.</summary>
    public const string DecimalToken = "DECIMAL";

    /// <summary>The <c>product.type</c> value for <see cref="ProductType.Service"/>.</summary>
    public const string ServiceToken = "SERVICE";

    /// <summary>The <c>product.type</c> value for <see cref="ProductType.NonInventory"/>.</summary>
    public const string NonInventoryToken = "NON_INVENTORY";

    /// <summary>The database token for a product type.</summary>
    public static string ToToken(ProductType type) => type switch
    {
        ProductType.Standard => StandardToken,
        ProductType.Fractional => DecimalToken,
        ProductType.Service => ServiceToken,
        ProductType.NonInventory => NonInventoryToken,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "There are exactly four product types."),
    };

    /// <summary>Reads a database token back into a product type.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The token is not one of the four the schema allows.
    /// </exception>
    public static ProductType Parse(string token)
    {
        if (TryParse(token, out var type))
        {
            return type;
        }

        throw new ArgumentOutOfRangeException(
            nameof(token),
            token,
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{token}' is not a product type. product.type is constrained to "
                + $"'{StandardToken}', '{DecimalToken}', '{ServiceToken}' or '{NonInventoryToken}'."));
    }

    /// <summary>Reads a database token back into a product type, without throwing.</summary>
    public static bool TryParse([NotNullWhen(true)] string? token, out ProductType type)
    {
        switch (token)
        {
            case StandardToken:
                type = ProductType.Standard;
                return true;
            case DecimalToken:
                type = ProductType.Fractional;
                return true;
            case ServiceToken:
                type = ProductType.Service;
                return true;
            case NonInventoryToken:
                type = ProductType.NonInventory;
                return true;
            default:
                type = ProductType.Standard;
                return false;
        }
    }

    /// <summary>
    /// True for <see cref="ProductType.Service"/> and <see cref="ProductType.NonInventory"/>: the
    /// two types with nothing to hold in stock, so nothing they sell ever posts a
    /// <c>stock_movement</c> (SRS FR-2.1-FR-2.8). Quantity and unit-of-measure rules still apply
    /// to them in full - there is no stock to convert, but the bill still has to say how many.
    /// </summary>
    public static bool PostsNoStockMovement(ProductType type) =>
        type is ProductType.Service or ProductType.NonInventory;
}
