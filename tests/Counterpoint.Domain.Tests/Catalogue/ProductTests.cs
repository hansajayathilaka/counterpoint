using System;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Catalogue;

/// <summary>
/// "Exactly one base UOM with factor 1.0000" is enforced in the domain as well as by
/// <c>trg_product_uom_base_factor_insert</c>/<c>_update</c> and <c>ux_product_uom_one_base</c>
/// (docs/01_DATA_MODEL.md §8, SRS FR-2.4).
/// </summary>
public sealed class ProductTests
{
    [Fact]
    public void FR_2_4_ABoxOfPiecesIsAValidProduct()
    {
        var product = CatalogueTestBuilder.BoxesOfPieces();

        product.BaseUomOption.UomId.Should().Be(CatalogueTestBuilder.PieceUomId);
        product.BaseUomOption.Conversion.IsBase.Should().BeTrue();
        product.UomOptions.Should().HaveCount(2);
    }

    [Fact]
    public void FR_2_4_RejectsNoUomOptionsAtAll()
    {
        var noUnits = () => new Product(1, "X", "X", ProductType.Standard, 1, uomOptions: []);

        noUnits.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FR_2_4_RejectsMoreThanOneBaseUnit()
    {
        var twoBases = () => new Product(
            1,
            "X",
            "X",
            ProductType.Standard,
            baseUomId: 1,
            uomOptions:
            [
                new ProductUomOption(1, "pc", 0, UomConversion.Base, IsBase: true, SellingPrice: null),
                new ProductUomOption(2, "box", 0, UomConversion.Base, IsBase: true, SellingPrice: null),
            ]);

        twoBases.Should().Throw<ArgumentException>().WithMessage("*exactly one base unit*");
    }

    [Fact]
    public void FR_2_4_RejectsZeroBaseUnits()
    {
        var noBase = () => new Product(
            1,
            "X",
            "X",
            ProductType.Standard,
            baseUomId: 1,
            uomOptions:
            [
                new ProductUomOption(1, "pc", 0, UomConversion.FromDecimal(2m), IsBase: false, SellingPrice: null),
            ]);

        noBase.Should().Throw<ArgumentException>().WithMessage("*exactly one base unit*");
    }

    [Fact]
    public void FR_2_4_RejectsABaseRowWhoseFactorIsNotExactlyOne()
    {
        // Cannot be built through UomConversion.FromScaled with is_base semantics directly, but a
        // caller could still mark a non-1 factor row IsBase - the domain refuses it, matching the
        // database trigger's own "factor is exactly 10000 when it is the base" rule.
        var wrongFactor = () => new Product(
            1,
            "X",
            "X",
            ProductType.Standard,
            baseUomId: 1,
            uomOptions:
            [
                new ProductUomOption(1, "pc", 0, UomConversion.FromDecimal(2m), IsBase: true, SellingPrice: null),
            ]);

        wrongFactor.Should().Throw<ArgumentException>().WithMessage("*conversion factor must be exactly 1*");
    }

    [Fact]
    public void FR_2_4_RejectsWhenTheBaseRowsUnitDoesNotMatchTheProductsBaseUomId()
    {
        var mismatch = () => new Product(
            1,
            "X",
            "X",
            ProductType.Standard,
            baseUomId: 99,
            uomOptions:
            [
                new ProductUomOption(1, "pc", 0, UomConversion.Base, IsBase: true, SellingPrice: null),
            ]);

        mismatch.Should().Throw<ArgumentException>().WithMessage("*must agree*");
    }

    [Fact]
    public void FR_2_4_RejectsARepeatedUnit()
    {
        var repeated = () => new Product(
            1,
            "X",
            "X",
            ProductType.Standard,
            baseUomId: 1,
            uomOptions:
            [
                new ProductUomOption(1, "pc", 0, UomConversion.Base, IsBase: true, SellingPrice: null),
                new ProductUomOption(1, "pc", 0, UomConversion.FromDecimal(2m), IsBase: false, SellingPrice: null),
            ]);

        repeated.Should().Throw<ArgumentException>().WithMessage("*listed more than once*");
    }

    [Fact]
    public void FR_2_1_ServiceAndNonInventoryTypesPostNoStockMovement()
    {
        var service = new Product(
            1,
            "LABOUR",
            "Installation",
            ProductType.Service,
            baseUomId: 1,
            uomOptions: [new ProductUomOption(1, "svc", 0, UomConversion.Base, IsBase: true, SellingPrice: null)]);

        var nonInventory = new Product(
            2,
            "DELIVERY",
            "Delivery fee",
            ProductType.NonInventory,
            baseUomId: 1,
            uomOptions: [new ProductUomOption(1, "svc", 0, UomConversion.Base, IsBase: true, SellingPrice: null)]);

        var standard = CatalogueTestBuilder.BoxesOfPieces();

        service.PostsNoStockMovement.Should().BeTrue();
        nonInventory.PostsNoStockMovement.Should().BeTrue();
        standard.PostsNoStockMovement.Should().BeFalse();
    }
}
