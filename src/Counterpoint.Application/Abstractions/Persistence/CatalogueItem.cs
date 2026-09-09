using System.Collections.Generic;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// What the catalogue knows about one sellable variant, as the sale path needs it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is an internal read model, not a cashier DTO.</b> It carries
/// <see cref="UnitCost"/>, and cost never reaches a cashier's screen (CLAUDE.md invariant 8,
/// SRS NFR-S2, AC-17). The type the UI receives is
/// <c>Counterpoint.Application.Sales.ScannedItem</c>, which has no cost field at all - so
/// there is nothing there to leak.
/// </para>
/// <para>
/// Prices and costs are snapshotted onto <c>sale_line</c> at completion, because the
/// catalogue moves and a return six months later must still refund what was paid
/// (CLAUDE.md invariant 10).
/// </para>
/// </remarks>
/// <param name="ProductVariantId">The variant being sold.</param>
/// <param name="ProductId">
/// The product this variant belongs to (SRS FR-2.1-FR-2.8) - what <see cref="UomOptions"/> is
/// scoped to, and enough to build a <c>Domain.Catalogue.Product</c> for <c>UomConverter</c>
/// and <c>PriceResolver</c> (P1-T09: unit switching, FR-2.5, FR-3.7).
/// </param>
/// <param name="ProductCode">The product's own code, for the plain-language messages <c>UomConverter</c> builds.</param>
/// <param name="ProductType">
/// Whether the product is sold in whole units only, to a fractional precision, or posts no stock
/// movement at all (<c>SERVICE</c>/<c>NON_INVENTORY</c>) - SRS FR-2.1-FR-2.8.
/// </param>
/// <param name="Description">The product name, as it will be snapshotted onto the bill line.</param>
/// <param name="BaseUomId">The product's base unit. Everything in the ledger is in base units.</param>
/// <param name="UomSymbol">The base unit's symbol, for the receipt.</param>
/// <param name="UnitPrice">Retail price per base unit.</param>
/// <param name="UnitCost">Moving-average cost per base unit. Owner-only information.</param>
/// <param name="TaxRate">The rate on the product's tax class.</param>
/// <param name="QtyOnHand">
/// The stock balance projection at the moment of the lookup, in base units - joined into
/// the same prepared statement as the price so a scan never costs a second round trip to show
/// what is left on the shelf (P1-T06, SRS FR-2.9-2.12, NFR-P1). Zero for a variant that has never
/// had a movement posted, not an absent row: the projection is derived from the stock movement
/// ledger and a variant with no history simply has none of either.
/// </param>
/// <param name="MaxDiscountRate">
/// <c>product.max_discount_rate</c>, or null to fall back to the shop-wide cashier limit
/// (SRS FR-3.18, Q-12, P1-T08's <c>DiscountCapPolicy.EffectiveCap</c>).
/// </param>
/// <param name="UomOptions">
/// Every unit this product sells in, base unit included (SRS FR-2.4, FR-2.5, FR-3.7) - what lets
/// the sale path switch a line's selling unit and reprice it without a second round trip.
/// </param>
public sealed record CatalogueItem(
    long ProductVariantId,
    long ProductId,
    string ProductCode,
    ProductType ProductType,
    string Description,
    long BaseUomId,
    string UomSymbol,
    Money UnitPrice,
    Money UnitCost,
    TaxRate TaxRate,
    Quantity QtyOnHand,
    Percentage? MaxDiscountRate,
    IReadOnlyList<ProductUomOption> UomOptions)
{
    /// <summary>
    /// Assembles the <c>Domain.Catalogue.Product</c> this item's <see cref="UomOptions"/>
    /// describe, for <c>UomConverter</c> and <c>PriceResolver</c> - built here, once, rather than
    /// by every caller, so the mapping from a read model to the domain aggregate lives in exactly
    /// one place.
    /// </summary>
    public Product ToProduct() => new(ProductId, ProductCode, Description, ProductType, BaseUomId, UomOptions);
}
