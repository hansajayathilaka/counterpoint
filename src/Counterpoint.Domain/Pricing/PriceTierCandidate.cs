using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// One <c>price_tier</c> row, as <see cref="PriceResolver"/> needs it (docs/01_DATA_MODEL.md §3,
/// SRS FR-2.14-FR-2.16). Already scoped to one variant by the caller - the resolver never joins
/// across variants.
/// </summary>
/// <remarks>
/// <b>Promotional or not is read off the dates, not a separate column.</b> The schema gives a
/// <c>price_tier</c> row two nullable date columns and nothing else that marks a row as a
/// promotion; a row that carries either date is time-bound and therefore a promotional price
/// (SRS FR-2.16), and a row with neither is always in force - the flat tier price, or one band of
/// a quantity break (SRS FR-2.14, FR-2.15). This is documented here because it is the one
/// judgement call the schema leaves to the reader: two concepts, "promotional" and
/// "quantity-tiered", share one table and are told apart only by whether a date window is set.
/// </remarks>
/// <param name="Id">The <c>price_tier.id</c>, carried through so the resolution can name its source row.</param>
/// <param name="Tier">RETAIL or TRADE - matched against the selling customer's own tier.</param>
/// <param name="MinQty">
/// The quantity break this price starts at, in the product's base unit
/// (<c>price_tier.min_qty</c>, always in base units regardless of the unit being sold in).
/// Zero for a tier's flat price with no band of its own.
/// </param>
/// <param name="Price">
/// What one base unit costs at this band, before unit conversion - the same convention as
/// <c>product_variant.price</c>, so it is resolved through the same "times the conversion
/// factor" step as the base price it can override.
/// </param>
/// <param name="ValidFrom">First day this price is in force, inclusive, or null for "always has been".</param>
/// <param name="ValidTo">Last day this price is in force, inclusive, or null for "no end date".</param>
public sealed record PriceTierCandidate(
    long Id,
    CustomerPriceTier Tier,
    Quantity MinQty,
    Money Price,
    DateOnly? ValidFrom,
    DateOnly? ValidTo)
{
    /// <summary>True when this row carries a date window - a promotion rather than a standing tier price.</summary>
    public bool IsPromotional => ValidFrom is not null || ValidTo is not null;

    /// <summary>True when <paramref name="date"/> falls inside this row's date window.</summary>
    /// <remarks>A row with no dates at all is always active - the flat tier price and every quantity band are.</remarks>
    public bool IsActiveOn(DateOnly date) =>
        (ValidFrom is null || date >= ValidFrom) && (ValidTo is null || date <= ValidTo);
}
