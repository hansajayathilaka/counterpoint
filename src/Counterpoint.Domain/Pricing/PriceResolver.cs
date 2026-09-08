using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// Resolves what one unit sells for, single-sourced so Phase 5's trade tiers and quantity
/// breaks land on top of it rather than beside it (docs/01_DATA_MODEL.md §3, SRS FR-2.13-FR-2.16,
/// task P1-T08).
/// </summary>
/// <remarks>
/// <para>
/// <b>The precedence, highest first:</b>
/// </para>
/// <list type="number">
/// <item><description>A promotional price in its date window (SRS FR-2.16).</description></item>
/// <item><description>A quantity break for the customer's tier (SRS FR-2.15).</description></item>
/// <item><description>The customer's tier price (SRS FR-2.14).</description></item>
/// <item><description><c>product_uom.selling_price</c> (SRS FR-2.5).</description></item>
/// <item><description><c>product_variant.price</c> times the unit's conversion factor (SRS FR-2.4, FR-2.13).</description></item>
/// </list>
/// <para>
/// The first three levels all read the same table, <c>price_tier</c>, priced per base unit
/// exactly as <c>product_variant.price</c> is - so a promotional or tier price that wins is
/// carried through <see cref="UomConverter.ResolvePrice"/>'s own "times the conversion factor"
/// step, the same as the base price it overrides. That is what "single-sourced" means here: a
/// win at level 1, 2 or 3 changes which base price feeds the unit conversion, and level 4 -
/// <c>product_uom.selling_price</c> - only ever gets a turn when none of the first three apply,
/// even though it is otherwise a more specific override than the plain base price.
/// </para>
/// <para>
/// Levels 2 and 3 are one selection over the same rows: the tier-matching, non-promotional
/// candidate with the greatest <see cref="PriceTierCandidate.MinQty"/> at or below the quantity
/// sold. A row with <c>min_qty = 0</c> is nothing but the flat tier price with no band of its
/// own, so the same "highest qualifying <c>min_qty</c>" rule finds it too; <see cref="PriceBasis"/>
/// only tells the two apart afterwards, for the reason text.
/// </para>
/// </remarks>
public static class PriceResolver
{
    /// <summary>
    /// Resolves the price for one unit of <paramref name="product"/>, at <paramref name="tier"/>,
    /// for <paramref name="quantityBase"/> already sold (in the product's base unit).
    /// </summary>
    /// <param name="product">The product being sold, with its unit-of-measure options.</param>
    /// <param name="variantBasePrice"><c>product_variant.price</c> - the level-5 fallback.</param>
    /// <param name="uomId">The unit being sold in.</param>
    /// <param name="quantityBase">
    /// How much is being sold, in the product's base unit - what a quantity break's
    /// <c>min_qty</c> is compared against, regardless of the unit the cashier actually rang it
    /// up in.
    /// </param>
    /// <param name="tier">The selling customer's price tier (SRS FR-2.14). Retail with no customer attached.</param>
    /// <param name="priceTiers">
    /// Every <c>price_tier</c> row for this variant, in any order. Rows for the other tier and
    /// rows outside <paramref name="asOf"/>'s date window are ignored, not an error.
    /// </param>
    /// <param name="asOf">The business date the sale is happening on, for FR-2.16's date window.</param>
    /// <exception cref="ArgumentNullException"><paramref name="product"/> or <paramref name="priceTiers"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The product does not sell in <paramref name="uomId"/>.</exception>
    public static PriceResolution Resolve(
        Product product,
        Money variantBasePrice,
        long uomId,
        Quantity quantityBase,
        CustomerPriceTier tier,
        IReadOnlyList<PriceTierCandidate> priceTiers,
        DateOnly asOf)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(priceTiers);

        var option = product.RequireUom(uomId);

        var applicable = priceTiers
            .Where(candidate => candidate.Tier == tier && candidate.IsActiveOn(asOf))
            .Where(candidate => IsAtOrBelow(candidate.MinQty, quantityBase))
            .ToArray();

        var promotional = BestCandidate(applicable, promotional: true);
        if (promotional is not null)
        {
            return BuildFromBaseOverride(promotional.Price, PriceBasis.Promotional, promotional.Id, option,
                "A promotional price is in force for this item today.");
        }

        var tierMatch = BestCandidate(applicable, promotional: false);
        if (tierMatch is not null)
        {
            return tierMatch.MinQty.IsPositive
                ? BuildFromBaseOverride(tierMatch.Price, PriceBasis.QuantityBreak, tierMatch.Id, option, string.Create(
                    CultureInfo.InvariantCulture,
                    $"Quantity break: {tierMatch.MinQty.Value} or more at the {TierWord(tier)} price."))
                : BuildFromBaseOverride(tierMatch.Price, PriceBasis.Tier, tierMatch.Id, option, string.Create(
                    CultureInfo.InvariantCulture,
                    $"The {TierWord(tier)} tier price."));
        }

        if (option.SellingPrice is { } sellingPrice)
        {
            return new PriceResolution(
                sellingPrice,
                PriceBasis.UnitSellingPrice,
                PriceTierId: null,
                string.Create(CultureInfo.InvariantCulture, $"The price set for {option.Symbol}."));
        }

        var basePrice = variantBasePrice.Multiply(option.Conversion.Factor);
        return new PriceResolution(
            basePrice,
            PriceBasis.BaseVariantPrice,
            PriceTierId: null,
            "The catalogue price, converted to this unit.");
    }

    /// <summary>
    /// The candidate, among <paramref name="promotional"/> rows or non-promotional ones, with the
    /// greatest qualifying <c>min_qty</c> - ties broken by the lowest id, so the choice is
    /// deterministic even though the data model does not forbid two rows at the same band.
    /// </summary>
    private static PriceTierCandidate? BestCandidate(IReadOnlyList<PriceTierCandidate> applicable, bool promotional) =>
        applicable
            .Where(candidate => candidate.IsPromotional == promotional)
            .OrderByDescending(candidate => candidate.MinQty.Value)
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault();

    /// <summary>
    /// Carries a level 1-3 base-unit price through the same "times the conversion factor" step
    /// <see cref="UomConverter.ResolvePrice"/> applies to <c>product_variant.price</c>, so an
    /// overridden base price is converted exactly as the price it overrides would have been.
    /// </summary>
    private static PriceResolution BuildFromBaseOverride(
        Money overriddenBasePrice,
        PriceBasis basis,
        long priceTierId,
        ProductUomOption option,
        string reason) =>
        new(overriddenBasePrice.Multiply(option.Conversion.Factor), basis, priceTierId, reason);

    private static string TierWord(CustomerPriceTier tier) => tier == CustomerPriceTier.Trade ? "trade" : "retail";

    /// <exception cref="InvalidOperationException">
    /// <paramref name="minQty"/> is not in the same unit as <paramref name="quantityBase"/> - a
    /// <c>price_tier.min_qty</c> that was somehow stored against a unit other than the product's
    /// base one (docs/01_DATA_MODEL.md §3 says it always is).
    /// </exception>
    private static bool IsAtOrBelow(Quantity minQty, Quantity quantityBase)
    {
        if (minQty.UomId != quantityBase.UomId)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"A price_tier row's min_qty is in uom {minQty.UomId}, but the quantity sold is in uom {quantityBase.UomId}. Both must be the product's base unit."));
        }

        return minQty <= quantityBase;
    }
}
