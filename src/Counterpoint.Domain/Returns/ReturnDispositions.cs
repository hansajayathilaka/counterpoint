using System;
using System.Globalization;

namespace Counterpoint.Domain.Returns;

/// <summary>
/// What a <see cref="ReturnDisposition"/> is called in the database, and back
/// (docs/01_DATA_MODEL.md §6). The same shape as <c>Counterpoint.Domain.Catalogue.ProductTypes</c>:
/// the two tokens are exactly the ones <c>ck_sale_return_line_disposition</c> constrains
/// <c>sale_return_line.disposition</c> to, spelled out once so no adapter can spell them
/// differently.
/// </summary>
public static class ReturnDispositions
{
    /// <summary>The <c>sale_return_line.disposition</c> value for <see cref="ReturnDisposition.Sellable"/>.</summary>
    public const string SellableToken = "SELLABLE";

    /// <summary>The <c>sale_return_line.disposition</c> value for <see cref="ReturnDisposition.Damaged"/>.</summary>
    public const string DamagedToken = "DAMAGED";

    /// <summary>The database token for a disposition.</summary>
    public static string ToToken(ReturnDisposition disposition) => disposition switch
    {
        ReturnDisposition.Sellable => SellableToken,
        ReturnDisposition.Damaged => DamagedToken,
        _ => throw new ArgumentOutOfRangeException(
            nameof(disposition), disposition, "There are exactly two return dispositions."),
    };

    /// <summary>Reads a database token back into a disposition.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The token is not one of the two the schema allows.
    /// </exception>
    public static ReturnDisposition Parse(string token) => token switch
    {
        SellableToken => ReturnDisposition.Sellable,
        DamagedToken => ReturnDisposition.Damaged,
        _ => throw new ArgumentOutOfRangeException(
            nameof(token),
            token,
            string.Create(
                CultureInfo.InvariantCulture,
                $"'{token}' is not a return disposition. sale_return_line.disposition is constrained to "
                + $"'{SellableToken}' or '{DamagedToken}'.")),
    };
}
