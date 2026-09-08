using System;
using System.Globalization;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// What a <see cref="CustomerPriceTier"/> is called in the database, and back
/// (docs/01_DATA_MODEL.md §3). The same shape as
/// <c>Counterpoint.Domain.Catalogue.ProductTypes</c>: the two tokens are exactly the ones
/// <c>ck_customer_type</c> and <c>ck_price_tier_tier</c> constrain their columns to, spelled out
/// once so no adapter can spell them differently.
/// </summary>
public static class CustomerPriceTiers
{
    /// <summary>The database value for <see cref="CustomerPriceTier.Retail"/>.</summary>
    public const string RetailToken = "RETAIL";

    /// <summary>The database value for <see cref="CustomerPriceTier.Trade"/>.</summary>
    public const string TradeToken = "TRADE";

    /// <summary>The database token for a tier.</summary>
    public static string ToToken(CustomerPriceTier tier) => tier switch
    {
        CustomerPriceTier.Retail => RetailToken,
        CustomerPriceTier.Trade => TradeToken,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, "Not a known price tier."),
    };

    /// <summary>Parses a database token back into a tier.</summary>
    /// <exception cref="ArgumentException"><paramref name="token"/> is neither RETAIL nor TRADE.</exception>
    public static CustomerPriceTier Parse(string token)
    {
        ArgumentNullException.ThrowIfNull(token);

        return token switch
        {
            RetailToken => CustomerPriceTier.Retail,
            TradeToken => CustomerPriceTier.Trade,
            _ => throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"'{token}' is not RETAIL or TRADE."),
                nameof(token)),
        };
    }
}
