namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// How a product behaves on the sale and stock paths (docs/01_DATA_MODEL.md §3,
/// <c>product.type</c>, SRS FR-2.1-FR-2.8).
/// </summary>
/// <remarks>
/// <see cref="Standard"/> is deliberately the zero value, the same reasoning as
/// <c>Counterpoint.Domain.Security.Role.Cashier</c>: the common hardware-shop item - nuts, bolts,
/// fittings sold by the piece - is what a product defaults to if something forgot to set the
/// type, and that default rejects fractional quantities rather than silently allowing them.
/// </remarks>
public enum ProductType
{
    /// <summary>Sold in whole units only, however many decimal places its unit allows.</summary>
    Standard = 0,

    /// <summary>
    /// Sold to the fractional precision of the unit it is sold in, for example 2.755 m of wire.
    /// The <c>product.type</c> token is <c>DECIMAL</c> (<see cref="ProductTypes.DecimalToken"/>);
    /// the member is spelled <see cref="Fractional"/> because CA1720 forbids a type name such as
    /// <c>Decimal</c> in a public identifier.
    /// </summary>
    Fractional = 1,

    /// <summary>Labour or a charge with nothing to hold in stock. Posts no stock movement.</summary>
    Service = 2,

    /// <summary>A billable line that is not inventory, for example a delivery fee. Posts no stock movement.</summary>
    NonInventory = 3,
}
