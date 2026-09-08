using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Tests.Support;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Catalogue;

/// <summary>
/// Stock is always held in base units; every sale, receipt and report converts to base units at
/// the boundary (SRS §2.2, FR-2.1-FR-2.8, FR-3.6, AC-08).
/// </summary>
public sealed class UomConverterTests
{
    private const int RoundTripSamples = 10_000;

    private const int Seed = 20_260_908;

    [Fact]
    public void FR_2_4_BoxToPieceConversion()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();

        // 1 box = 100 pcs; selling 2 boxes converts to exactly 200 base units.
        var baseQty = UomConverter.ToBase(2m, CatalogueTestBuilder.BoxUomId, product);

        baseQty.UomId.Should().Be(CatalogueTestBuilder.PieceUomId);
        baseQty.Value.Should().Be(200m);
        baseQty.ToScaled().Should().Be(2_000_000L);
    }

    [Fact]
    public void FR_2_4_CoilToMetreConversion()
    {
        var product = CatalogueTestBuilder.CoilsOfWire();

        // 1 coil = 90 m; selling 3.5 m converts to exactly 3.5 base units...
        var threeAndAHalfMetres = UomConverter.ToBase(3.5m, CatalogueTestBuilder.MetreUomId, product);
        threeAndAHalfMetres.Value.Should().Be(3.5m);

        // ...and selling 1 coil converts to exactly 90 base units.
        var oneCoil = UomConverter.ToBase(1m, CatalogueTestBuilder.CoilUomId, product);
        oneCoil.Value.Should().Be(90m);
        oneCoil.UomId.Should().Be(CatalogueTestBuilder.MetreUomId);
    }

    [Fact]
    public void FR_2_1_AStandardProductRejectsAFractionalQuantityWithAClearMessage()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();

        var sellHalfABox = () => UomConverter.ToBase(2.5m, CatalogueTestBuilder.BoxUomId, product);

        sellHalfABox.Should().Throw<InvalidOperationException>()
            .WithMessage("*whole*", "the message is plain language, not a raw validation code")
            .Which.Message.Should().Contain("2.5");
    }

    [Fact]
    public void FR_2_1_AStandardProductRejectsAFractionalQuantityRegardlessOfTheUnitsOwnDecimalPlaces()
    {
        // The base unit itself (pieces) carries decimal_places = 0, same as the box - the
        // STANDARD rule must not be confused with the DECIMAL rule even where they would agree.
        var product = CatalogueTestBuilder.BoxesOfPieces();

        var sellHalfAPiece = () => UomConverter.ToBase(0.5m, CatalogueTestBuilder.PieceUomId, product);

        sellHalfAPiece.Should().Throw<InvalidOperationException>().WithMessage("*whole*");
    }

    [Fact]
    public void FR_2_2_ADecimalProductAcceptsAQuantityToTheUnitsDecimalPlacesAndStoresItExactly()
    {
        var product = CatalogueTestBuilder.CoilsOfWire();

        // Metre carries decimal_places = 3; 2.755 m is exactly at that precision.
        var baseQty = UomConverter.ToBase(2.755m, CatalogueTestBuilder.MetreUomId, product);

        baseQty.Value.Should().Be(2.755m);
        baseQty.ToScaled().Should().Be(27_550L);
    }

    [Fact]
    public void FR_2_2_ADecimalProductRejectsMoreFractionalDigitsThanTheUnitAllows()
    {
        var product = CatalogueTestBuilder.CoilsOfWire();

        // Metre carries decimal_places = 3; a fourth fractional digit is refused.
        var sellTooPrecisely = () => UomConverter.ToBase(2.7551m, CatalogueTestBuilder.MetreUomId, product);

        sellTooPrecisely.Should().Throw<InvalidOperationException>()
            .WithMessage("*decimal place*");
    }

    [Fact]
    public void FR_2_5_TheProductUomSellingPriceWinsWhenSet()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: Money.FromDecimal(950m));

        var price = UomConverter.ResolvePrice(Money.FromDecimal(10m), CatalogueTestBuilder.BoxUomId, product);

        price.Should().Be(Money.FromDecimal(950m), "an explicit product_uom.selling_price is never collapsed to base price x factor");
    }

    [Fact]
    public void FR_2_5_PriceFallsBackToBasePriceTimesFactorWhenNoSellingPriceIsSet()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces(boxSellingPrice: null);

        var price = UomConverter.ResolvePrice(Money.FromDecimal(10m), CatalogueTestBuilder.BoxUomId, product);

        price.Should().Be(Money.FromDecimal(1000m));
    }

    [Fact]
    public void FR_2_5_TheBaseUnitAlwaysPricesAtTheBasePrice()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();

        var price = UomConverter.ResolvePrice(Money.FromDecimal(10m), CatalogueTestBuilder.PieceUomId, product);

        price.Should().Be(Money.FromDecimal(10m));
    }

    [Fact]
    public void ConvertingToOrFromAUnitTheProductDoesNotSellInIsRefusedWithAClearMessage()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();

        var toUnknownUnit = () => UomConverter.ToBase(1m, 999L, product);
        var fromUnknownUnit = () => UomConverter.FromBase(1m, 999L, product);

        toUnknownUnit.Should().Throw<InvalidOperationException>().WithMessage("*not sold*");
        fromUnknownUnit.Should().Throw<InvalidOperationException>().WithMessage("*not sold*");
    }

    /// <summary>
    /// The round-trip conversion property test the risk section of P1-T05 calls for: rounding
    /// drift in a UOM conversion is the specific failure mode named there, so 10 000 random
    /// quantities are exercised through a non-trivial conversion factor (90, matching the coil
    /// example - not a round power of ten), never a fixed table of examples.
    /// </summary>
    [Fact]
    public void FR_2_4_RoundTripConversionIsExactForTenThousandRandomQuantities()
    {
        var product = CatalogueTestBuilder.FullPrecisionProduct();
        var sample = new DeterministicSample(Seed);
        var failures = new List<string>();

        for (var iteration = 0; iteration < RoundTripSamples; iteration++)
        {
            // Any quantity the storage scale can represent - all four fractional digits, not a
            // fixed table of examples - converted to base units and back through the unit
            // itself (factor 1).
            var qty = sample.NextStorableDecimal(0.0001m, 100_000m);

            var baseQty = UomConverter.ToBase(qty, CatalogueTestBuilder.PrecisionUomId, product);
            var roundTripped = UomConverter.FromBase(baseQty.Value, CatalogueTestBuilder.PrecisionUomId, product);

            if (roundTripped.Value != qty)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {iteration}: {qty} -> {baseQty.Value} -> {roundTripped.Value}"));
            }
        }

        failures.Should().BeEmpty(
            "FromBase(ToBase(q)) must equal q exactly for every representable quantity "
            + "(P1-T05 risk: rounding drift in conversion); seed {0}",
            Seed);
    }

    [Fact]
    public void FR_2_4_RoundTripConversionIsExactThroughANonBaseUnitForTenThousandRandomQuantities()
    {
        var product = CatalogueTestBuilder.FullPrecisionProduct();
        var sample = new DeterministicSample(Seed + 1);
        var failures = new List<string>();

        for (var iteration = 0; iteration < RoundTripSamples; iteration++)
        {
            // The second unit carries decimal_places = 0 and an awkward, non-power-of-ten
            // conversion factor (37) - whole units of it, drawn at random, still have to divide
            // back out exactly through ToBase then FromBase.
            var wholeUnits = sample.NextInt(0, 100_000);

            var baseQty = UomConverter.ToBase(wholeUnits, CatalogueTestBuilder.AwkwardFactorUomId, product);
            var roundTripped = UomConverter.FromBase(baseQty.Value, CatalogueTestBuilder.AwkwardFactorUomId, product);

            if (roundTripped.Value != wholeUnits)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"iteration {iteration}: {wholeUnits} -> {baseQty.Value} -> {roundTripped.Value}"));
            }
        }

        failures.Should().BeEmpty(
            "FromBase(ToBase(q)) must equal q exactly through a non-base unit too; seed {0}",
            Seed + 1);
    }
}
