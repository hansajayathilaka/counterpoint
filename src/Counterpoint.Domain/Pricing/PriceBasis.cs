namespace Counterpoint.Domain.Pricing;

/// <summary>
/// Which level of <see cref="PriceResolver"/>'s precedence chain won (SRS FR-2.13-FR-2.16). The
/// cashier's screen shows this so a price can be explained rather than just trusted.
/// </summary>
/// <remarks>
/// Ordered exactly as <see cref="PriceResolver"/> checks them, highest precedence first - the
/// numeric value doubles as "how many levels were tried before this one won" for anything that
/// wants to log or sort by it, though nothing relies on that today.
/// </remarks>
public enum PriceBasis
{
    /// <summary>A time-bound promotional price, active on the day of sale (SRS FR-2.16).</summary>
    Promotional = 0,

    /// <summary>
    /// A <c>price_tier</c> row for the customer's tier whose <c>min_qty</c> is above zero and at
    /// or below the quantity sold (SRS FR-2.15).
    /// </summary>
    QuantityBreak = 1,

    /// <summary>
    /// The customer's tier price with no quantity band of its own (<c>min_qty = 0</c>), for
    /// example the flat trade price (SRS FR-2.14).
    /// </summary>
    Tier = 2,

    /// <summary><c>product_uom.selling_price</c>, set explicitly for this unit (SRS FR-2.5).</summary>
    UnitSellingPrice = 3,

    /// <summary>
    /// <c>product_variant.price</c> - the retail base-unit price - multiplied by the unit's
    /// conversion factor (SRS FR-2.4, FR-2.5, FR-2.13). What is left when nothing else applies.
    /// </summary>
    BaseVariantPrice = 4,
}
