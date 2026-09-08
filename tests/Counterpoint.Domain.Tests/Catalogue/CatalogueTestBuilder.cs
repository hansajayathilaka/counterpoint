using System.Collections.Generic;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Tests.Catalogue;

/// <summary>Builds a minimal, valid <see cref="Product"/> for the catalogue domain tests.</summary>
internal static class CatalogueTestBuilder
{
    internal const long PieceUomId = 1;
    internal const long BoxUomId = 2;
    internal const long MetreUomId = 3;
    internal const long CoilUomId = 4;
    internal const long PrecisionUomId = 5;
    internal const long AwkwardFactorUomId = 6;

    /// <summary>1 box = 100 pieces, sold in whole units only (SRS FR-2.1, FR-2.4).</summary>
    internal static Product BoxesOfPieces(Money? boxSellingPrice = null) => new(
        id: 1,
        code: "BOLT-M8",
        name: "M8 Bolt",
        type: ProductType.Standard,
        baseUomId: PieceUomId,
        uomOptions:
        [
            new ProductUomOption(PieceUomId, "pc", 0, UomConversion.Base, IsBase: true, SellingPrice: null),
            new ProductUomOption(BoxUomId, "box", 0, UomConversion.FromDecimal(100m), IsBase: false, SellingPrice: boxSellingPrice),
        ]);

    /// <summary>1 coil = 90 metres, sold by the fractional metre (SRS FR-2.1, FR-2.4).</summary>
    internal static Product CoilsOfWire() => new(
        id: 2,
        code: "WIRE-2.5",
        name: "2.5mm Wire",
        type: ProductType.Fractional,
        baseUomId: MetreUomId,
        uomOptions:
        [
            new ProductUomOption(MetreUomId, "m", 3, UomConversion.Base, IsBase: true, SellingPrice: null),
            new ProductUomOption(CoilUomId, "coil", 0, UomConversion.FromDecimal(90m), IsBase: false, SellingPrice: null),
        ]);

    /// <summary>
    /// A product carrying its base unit at the storage scale's full four decimal places, and a
    /// second unit at an awkward (non-power-of-ten) conversion factor - built for the round-trip
    /// conversion property test, which needs headroom to draw any storable quantity without a
    /// precision rule of its own getting in the way.
    /// </summary>
    internal static Product FullPrecisionProduct() => new(
        id: 3,
        code: "PRECISION-1",
        name: "Precision test product",
        type: ProductType.Fractional,
        baseUomId: PrecisionUomId,
        uomOptions:
        [
            new ProductUomOption(PrecisionUomId, "u", 4, UomConversion.Base, IsBase: true, SellingPrice: null),
            new ProductUomOption(AwkwardFactorUomId, "awk", 0, UomConversion.FromDecimal(37m), IsBase: false, SellingPrice: null),
        ]);

    internal static IReadOnlyDictionary<string, string> Attributes(params (string Key, string Value)[] pairs)
    {
        var dictionary = new Dictionary<string, string>();

        foreach (var (key, value) in pairs)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }
}
