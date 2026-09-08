using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// Barcode administration and the internal-barcode generator (docs/01_DATA_MODEL.md §3, SRS
/// FR-2.9, FR-2.10, FR-2.24).
/// </summary>
public sealed class BarcodeMaintenanceTests
{
    [Fact]
    public async Task FR_2_9_MultipleBarcodesResolveToTheSameSku()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (barcodes, lookup, variantId) = await SetUpAsync(fixture, "WSHR-M8");

        await barcodes.AddAsync(variantId, "1111111111111", makePrimary: true);
        await barcodes.AddAsync(variantId, "2222222222222", makePrimary: false);

        var first = await lookup.FindByBarcodeAsync("1111111111111");
        var second = await lookup.FindByBarcodeAsync("2222222222222");

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.ProductVariantId.Should().Be(variantId);
        second!.ProductVariantId.Should().Be(variantId);
    }

    [Fact]
    public async Task FR_2_24_AddingADuplicateBarcodeIsHardBlocked()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (barcodes, _, variantId) = await SetUpAsync(fixture, "WSHR-M10");
        var (_, _, otherVariantId) = await SetUpAsync(fixture, "WSHR-M12");

        await barcodes.AddAsync(variantId, "9999999999999", makePrimary: true);

        var addAgain = () => barcodes.AddAsync(otherVariantId, "9999999999999", makePrimary: true);

        await addAgain.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*already attached*", "FR-2.24's hard block never has an override");
    }

    [Fact]
    public async Task FR_2_10_GenerateInternalProducesAValidCheckedBarcodeThatResolves()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (barcodes, lookup, variantId) = await SetUpAsync(fixture, "LOOSE-NAIL-25MM");

        var generated = await barcodes.GenerateInternalAsync(variantId, makePrimary: true);

        InternalBarcodeGenerator.IsValid(generated).Should().BeTrue();

        var resolved = await lookup.FindByBarcodeAsync(generated);
        resolved.Should().NotBeNull();
        resolved!.ProductVariantId.Should().Be(variantId);
    }

    [Fact]
    public async Task FR_2_10_TwoGeneratedBarcodesNeverCollide()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (barcodes, _, variantOne) = await SetUpAsync(fixture, "LOOSE-SCREW-A");
        var (_, _, variantTwo) = await SetUpAsync(fixture, "LOOSE-SCREW-B");

        var first = await barcodes.GenerateInternalAsync(variantOne, makePrimary: true);
        var second = await barcodes.GenerateInternalAsync(variantTwo, makePrimary: true);

        first.Should().NotBe(second);
    }

    [Fact]
    public async Task TheFirstBarcodeAddedIsAlwaysPrimaryRegardlessOfWhatWasAsked()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (barcodes, _, variantId) = await SetUpAsync(fixture, "WSHR-M14");

        var id = await barcodes.AddAsync(variantId, "3333333333333", makePrimary: false);

        var rows = await barcodes.ListForVariantAsync(variantId);
        rows.Should().ContainSingle(row => row.Id == id && row.IsPrimary);
    }

    [Fact]
    public async Task RemovingThePrimaryBarcodePromotesAnotherOne()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (barcodes, _, variantId) = await SetUpAsync(fixture, "WSHR-M16");

        var primaryId = await barcodes.AddAsync(variantId, "4444444444444", makePrimary: true);
        var secondaryId = await barcodes.AddAsync(variantId, "5555555555555", makePrimary: false);

        await barcodes.RemoveAsync(primaryId);

        var rows = await barcodes.ListForVariantAsync(variantId);
        rows.Should().ContainSingle(row => row.Id == secondaryId && row.IsPrimary);
    }

    [Fact]
    public async Task FR_2_24_AVerySimilarNameAndBrandWarnsAndTheWarningCanBeOverridden()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var brands = fixture.Resolve<IBrandMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;
        var brandId = await brands.CreateAsync(new SaveBrandCommand("Bosch"));

        await products.CreateAsync(NewProduct("DRILL-13-A", "Bosch Drill 13mm", brandId, pieceId, exemptId));

        var createSimilar = () => products.CreateAsync(
            NewProduct("DRILL-13-B", "Bosch Drll 13mm", brandId, pieceId, exemptId));

        await createSimilar.Should().ThrowAsync<DuplicateProductWarningException>()
            .Where(exception => exception.Matches.Any(match => match.ProductName == "Bosch Drill 13mm"));

        // The override: resubmitting the same command with ConfirmDuplicate set proceeds anyway.
        var confirmed = () => products.CreateAsync(
            NewProduct("DRILL-13-B", "Bosch Drll 13mm", brandId, pieceId, exemptId) with { ConfirmDuplicate = true });

        await confirmed.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ADifferentBrandWithASimilarNameDoesNotWarn()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var brands = fixture.Resolve<IBrandMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;
        var boschId = await brands.CreateAsync(new SaveBrandCommand("Bosch"));
        var makitaId = await brands.CreateAsync(new SaveBrandCommand("Makita"));

        await products.CreateAsync(NewProduct("DRILL-14-A", "Cordless Drill 13mm", boschId, pieceId, exemptId));

        var createDifferentBrand = () => products.CreateAsync(
            NewProduct("DRILL-14-B", "Cordless Drill 13mm", makitaId, pieceId, exemptId));

        await createDifferentBrand.Should().NotThrowAsync(
            "FR-2.24 asks for the same brand as well as a similar name");
    }

    private static async Task<(IBarcodeMaintenance Barcodes, IProductLookup Lookup, long VariantId)> SetUpAsync(
        SaleFixture fixture,
        string codeSuffix)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var barcodes = fixture.Resolve<IBarcodeMaintenance>();
        var lookup = fixture.Resolve<IProductLookup>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        // ConfirmDuplicate: true - these helpers create several products whose codes differ by
        // only a character or two (WSHR-M8, WSHR-M10, ...), which is exactly what FR-2.24's
        // similar-name check is supposed to catch. That check is exercised on its own terms
        // below; here it would only be noise.
        var productId = await products.CreateAsync(
            NewProduct(codeSuffix, codeSuffix, brandId: null, pieceId, exemptId) with { ConfirmDuplicate = true });
        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand(codeSuffix + "-V", new System.Collections.Generic.Dictionary<string, string>(), Money.FromDecimal(5.00m)));

        return (barcodes, lookup, variantId);
    }

    private static SaveProductCommand NewProduct(string code, string name, long? brandId, long baseUomId, long taxClassId) =>
        new(
            code,
            name,
            NameAlt: null,
            CategoryId: null,
            brandId,
            baseUomId,
            Counterpoint.Domain.Catalogue.ProductType.Standard,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null);
}
