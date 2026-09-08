namespace Counterpoint.Domain.Pricing;

/// <summary>
/// Which price band a customer buys at (docs/01_DATA_MODEL.md §3, <c>customer.type</c> and
/// <c>price_tier.tier</c>, SRS FR-2.14).
/// </summary>
/// <remarks>
/// <see cref="Retail"/> is the zero value, the same reasoning as
/// <c>Counterpoint.Domain.Catalogue.ProductType.Standard</c>: a sale with nobody's customer
/// record attached - the overwhelming majority of them, in a hardware shop - prices at retail
/// rather than silently getting a trade discount nobody asked for.
/// </remarks>
public enum CustomerPriceTier
{
    /// <summary>The everyday counter price.</summary>
    Retail = 0,

    /// <summary>The wholesale price given to a customer flagged trade (SRS FR-2.14).</summary>
    Trade = 1,
}
