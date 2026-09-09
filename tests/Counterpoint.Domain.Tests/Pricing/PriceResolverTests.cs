using System;
using System.Collections.Generic;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Tests.Catalogue;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Pricing;

/// <summary>
/// The five-level price precedence, single-sourced so Phase 5's trade tiers and quantity breaks
/// land on top of it (SRS FR-2.13-FR-2.16, task P1-T08).
/// </summary>
public sealed class PriceResolverTests
{
    private const long PieceUomId = CatalogueTestBuilder.PieceUomId;
    private const long BoxUomId = CatalogueTestBuilder.BoxUomId;

    private static readonly DateOnly SaleDate = new(2026, 9, 8);
    private static readonly Money VariantBasePrice = Money.FromDecimal(10m);

    [Fact]
    public void FR_2_16_Level1_APromotionalPriceInItsDateWindowWinsOverEveryOtherLevel()
    {
        // Documented winner: the promotional row, at 8.00 - even though a qualifying quantity
        // break, a tier price, a unit selling price and the base price are all also on offer.
        var product = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: Money.FromDecimal(950m));

        var candidates = new[]
        {
            Promotional(id: 1, price: 8m, from: SaleDate, to: SaleDate),
            QuantityBreak(id: 2, minQty: 1m, price: 9m),
            Tier(id: 3, price: 9.5m),
        };

        var resolution = Resolve(product, candidates, qtySold: 5m);

        resolution.Basis.Should().Be(PriceBasis.Promotional);
        resolution.Price.Should().Be(Money.FromDecimal(8m));
        resolution.PriceTierId.Should().Be(1);
        resolution.Reason.Should().Contain("promotional");
    }

    [Fact]
    public void FR_2_15_Level2_AQuantityBreakWinsWhenNoPromotionIsInItsDateWindow()
    {
        // Documented winner: the quantity break at 9.00 - the promotional row exists but its
        // window has already closed, so it is ignored, not merely deprioritised.
        var product = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: Money.FromDecimal(950m));

        var candidates = new[]
        {
            Promotional(id: 1, price: 8m, from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 1, 31)),
            QuantityBreak(id: 2, minQty: 1m, price: 9m),
            Tier(id: 3, price: 9.5m),
        };

        var resolution = Resolve(product, candidates, qtySold: 5m);

        resolution.Basis.Should().Be(PriceBasis.QuantityBreak);
        resolution.Price.Should().Be(Money.FromDecimal(9m));
        resolution.PriceTierId.Should().Be(2);
        resolution.Reason.Should().Contain("Quantity break");
    }

    [Fact]
    public void FR_2_14_Level3_TheFlatTierPriceWinsWhenTheQuantityDoesNotReachAnyBreak()
    {
        // Documented winner: the flat tier price at 9.50 - the quantity break needs 10 or more
        // and only 5 are being sold, so it does not qualify.
        var product = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: Money.FromDecimal(950m));

        var candidates = new[]
        {
            QuantityBreak(id: 2, minQty: 10m, price: 9m),
            Tier(id: 3, price: 9.5m),
        };

        var resolution = Resolve(product, candidates, qtySold: 5m);

        resolution.Basis.Should().Be(PriceBasis.Tier);
        resolution.Price.Should().Be(Money.FromDecimal(9.5m));
        resolution.PriceTierId.Should().Be(3);
        resolution.Reason.Should().Contain("tier price");
    }

    [Fact]
    public void FR_2_5_Level4_TheUnitSellingPriceWinsWhenNoPriceTierRowApplies()
    {
        // Documented winner: product_uom.selling_price (9.50 for a box) - there is no price_tier
        // row at all, so the first three levels never get a turn.
        var product = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: Money.FromDecimal(950m));

        var resolution = Resolve(product, [], qtySold: 5m, uomId: BoxUomId);

        resolution.Basis.Should().Be(PriceBasis.UnitSellingPrice);
        resolution.Price.Should().Be(Money.FromDecimal(950m));
        resolution.PriceTierId.Should().BeNull();
        resolution.Reason.Should().Contain("box");
    }

    [Fact]
    public void FR_2_13_Level5_TheBaseVariantPriceTimesTheConversionFactorWinsWhenNothingElseApplies()
    {
        // Documented winner: product_variant.price (10.00) x the box's conversion factor (100) -
        // no price_tier row and no product_uom.selling_price set for the box.
        var product = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: null);

        var resolution = Resolve(product, [], qtySold: 5m, uomId: BoxUomId);

        resolution.Basis.Should().Be(PriceBasis.BaseVariantPrice);
        resolution.Price.Should().Be(Money.FromDecimal(1000m));
        resolution.PriceTierId.Should().BeNull();
        resolution.Reason.Should().Contain("catalogue price");
    }

    /// <summary>
    /// All five levels, in one place: starting from a bill that could be priced by any of them
    /// and stripping away the highest-precedence fact still standing, one at a time, proves the
    /// order is exactly promotional, then quantity break, then tier, then unit selling price,
    /// then base price x factor - never some other order that happens to agree on one example.
    /// </summary>
    [Fact]
    public void FR_2_13_ToFR_2_16_TheFiveLevelsWinInPrecedenceOrderAsEachHigherFactIsRemovedInTurn()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: Money.FromDecimal(950m));
        var everyCandidate = new List<PriceTierCandidate>
        {
            Promotional(id: 1, price: 8m, from: SaleDate, to: SaleDate),
            QuantityBreak(id: 2, minQty: 1m, price: 9m),
            Tier(id: 3, price: 9.5m),
        };

        // Level 1: the promotion is in its window, so it wins over everything else on offer.
        var level1 = Resolve(product, everyCandidate, qtySold: 5m);
        level1.Basis.Should().Be(PriceBasis.Promotional);
        level1.Price.Should().Be(Money.FromDecimal(8m));

        // Level 2: with the promotion gone, the quantity break wins over the flat tier price,
        // the unit selling price and the base price.
        everyCandidate.RemoveAt(0);
        var level2 = Resolve(product, everyCandidate, qtySold: 5m);
        level2.Basis.Should().Be(PriceBasis.QuantityBreak);
        level2.Price.Should().Be(Money.FromDecimal(9m));

        // Level 3: with the quantity break gone too, the flat tier price wins over the unit
        // selling price and the base price.
        everyCandidate.RemoveAt(0);
        var level3 = Resolve(product, everyCandidate, qtySold: 5m);
        level3.Basis.Should().Be(PriceBasis.Tier);
        level3.Price.Should().Be(Money.FromDecimal(9.5m));

        // Level 4: with no price_tier row left at all, product_uom.selling_price wins over the
        // base price. Resolved for the box unit, which is the one carrying an explicit
        // selling_price in this fixture - the piece (base) unit never has one of its own.
        everyCandidate.Clear();
        var level4 = Resolve(product, everyCandidate, qtySold: 5m, uomId: BoxUomId);
        level4.Basis.Should().Be(PriceBasis.UnitSellingPrice);
        level4.Price.Should().Be(Money.FromDecimal(950m));

        // Level 5: with the unit's own selling price unset too, only the base price x factor is
        // left.
        var productWithNoUnitPrice = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: null);
        var level5 = Resolve(productWithNoUnitPrice, everyCandidate, qtySold: 5m, uomId: BoxUomId);
        level5.Basis.Should().Be(PriceBasis.BaseVariantPrice);
        level5.Price.Should().Be(Money.FromDecimal(1000m));
    }

    [Fact]
    public void FR_2_15_AQuantityBreakBelowTheQuantitySoldNeverWinsOverOneAtOrBelowIt()
    {
        // Two bands: 5 or more at 9.50, 10 or more at 9.00. Selling exactly 7 qualifies for the
        // first band but not the second, and the resolver must not silently pick the lower one.
        var product = CatalogueTestBuilder.BoxesOfPieces();
        var candidates = new[]
        {
            QuantityBreak(id: 1, minQty: 5m, price: 9.5m),
            QuantityBreak(id: 2, minQty: 10m, price: 9m),
        };

        var resolution = Resolve(product, candidates, qtySold: 7m);

        resolution.Price.Should().Be(Money.FromDecimal(9.5m));
        resolution.PriceTierId.Should().Be(1);
    }

    [Fact]
    public void FR_2_15_ATieBetweenTwoQualifyingBandsIsBrokenByTheLowestId()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();
        var candidates = new[]
        {
            QuantityBreak(id: 20, minQty: 5m, price: 9m),
            QuantityBreak(id: 5, minQty: 5m, price: 8.75m),
        };

        var resolution = Resolve(product, candidates, qtySold: 5m);

        resolution.PriceTierId.Should().Be(5, "the lowest id wins a tie on min_qty, deterministically");
        resolution.Price.Should().Be(Money.FromDecimal(8.75m));
    }

    [Fact]
    public void FR_2_14_ARowForTheOtherCustomerTierIsIgnored()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();
        var tradeOnly = new[] { Tier(id: 1, price: 7m, tier: CustomerPriceTier.Trade) };

        var resolution = Resolve(product, tradeOnly, qtySold: 1m, tier: CustomerPriceTier.Retail);

        resolution.Basis.Should().Be(PriceBasis.BaseVariantPrice, "a trade-tier row never prices a retail sale");
    }

    [Fact]
    public void FR_2_16_APromotionalRowOutsideItsDateWindowIsIgnoredNotJustDeprioritised()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();
        var expired = new[] { Promotional(id: 1, price: 1m, from: new DateOnly(2020, 1, 1), to: new DateOnly(2020, 1, 2)) };

        var resolution = Resolve(product, expired, qtySold: 1m);

        resolution.Basis.Should().Be(PriceBasis.BaseVariantPrice);
    }

    [Fact]
    public void APriceTierRowInAUomOtherThanTheQuantitySoldsThrows()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();
        var wrongUom = new[]
        {
            new PriceTierCandidate(1, CustomerPriceTier.Retail, Quantity.FromDecimal(1m, 999L), Money.FromDecimal(9m), null, null),
        };

        var act = () => PriceResolver.Resolve(
            product, VariantBasePrice, PieceUomId, Quantity.FromDecimal(1m, PieceUomId),
            CustomerPriceTier.Retail, wrongUom, SaleDate);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NullArgumentsAreRefused()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();

        var nullProduct = () => PriceResolver.Resolve(
            null!, VariantBasePrice, PieceUomId, Quantity.FromDecimal(1m, PieceUomId),
            CustomerPriceTier.Retail, [], SaleDate);
        var nullCandidates = () => PriceResolver.Resolve(
            product, VariantBasePrice, PieceUomId, Quantity.FromDecimal(1m, PieceUomId),
            CustomerPriceTier.Retail, null!, SaleDate);

        nullProduct.Should().Throw<ArgumentNullException>();
        nullCandidates.Should().Throw<ArgumentNullException>();
    }

    private static PriceResolution Resolve(
        Product product,
        IReadOnlyList<PriceTierCandidate> candidates,
        decimal qtySold,
        long uomId = PieceUomId,
        CustomerPriceTier tier = CustomerPriceTier.Retail) =>
        PriceResolver.Resolve(
            product,
            VariantBasePrice,
            uomId,
            Quantity.FromDecimal(qtySold, PieceUomId),
            tier,
            candidates,
            SaleDate);

    private static PriceTierCandidate Promotional(long id, decimal price, DateOnly from, DateOnly to) =>
        new(id, CustomerPriceTier.Retail, Quantity.Zero(PieceUomId), Money.FromDecimal(price), from, to);

    private static PriceTierCandidate QuantityBreak(long id, decimal minQty, decimal price) =>
        new(id, CustomerPriceTier.Retail, Quantity.FromDecimal(minQty, PieceUomId), Money.FromDecimal(price), null, null);

    private static PriceTierCandidate Tier(long id, decimal price, CustomerPriceTier tier = CustomerPriceTier.Retail) =>
        new(id, tier, Quantity.Zero(PieceUomId), Money.FromDecimal(price), null, null);
}
