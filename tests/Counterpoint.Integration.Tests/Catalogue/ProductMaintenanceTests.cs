using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// The owner's product, variant and UOM conversion maintenance - stock is always held in base
/// units, and a distinct price per unit is the point (docs/01_DATA_MODEL.md §3, §8, SRS
/// FR-2.1-FR-2.8, FR-3.6, AC-08).
/// </summary>
public sealed class ProductMaintenanceTests
{
    [Fact]
    public async Task FR_2_4_CreatingAProductAlsoCreatesItsBaseUnitRowInTheSameTransaction()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);

        var productId = await products.CreateAsync(NewStandardProduct("BOLT-M8", pieceId, exemptId));

        (await fixture.CountAsync($"SELECT COUNT(*) FROM product_uom WHERE product_id = {productId} AND is_base = 1;"))
            .Should().Be(1, "a product can never exist without its base unit (docs/01_DATA_MODEL.md §8)");

        (await fixture.ScalarAsync($"SELECT conversion_factor FROM product_uom WHERE product_id = {productId} AND is_base = 1;"))
            .Should().Be("10000");

        var options = await products.ListUomOptionsAsync(productId);
        options.Should().ContainSingle(option => option.IsBase && option.UomId == pieceId);
    }

    [Fact]
    public async Task FR_2_4_TheBaseUnitCannotBeChangedOnceTheProductExists()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var uoms = fixture.Resolve<IUomMaintenance>();
        var metreId = await uoms.CreateAsync(new SaveUomCommand("Metre", "m", 3));

        var productId = await products.CreateAsync(NewStandardProduct("BOLT-M10", pieceId, exemptId));

        var changeBase = () => products.UpdateAsync(
            productId,
            NewStandardProduct("BOLT-M10", metreId, exemptId));

        await changeBase.Should().ThrowAsync<InvalidOperationException>().WithMessage("*base unit cannot be changed*");
    }

    [Fact]
    public async Task ADuplicateProductCodeIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);

        await products.CreateAsync(NewStandardProduct("NUT-M8", pieceId, exemptId));

        var again = () => products.CreateAsync(NewStandardProduct("NUT-M8", pieceId, exemptId));

        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already a product*");
    }

    [Fact]
    public async Task CreatingAProductWithAnUnknownBaseUnitOrTaxClassIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);

        var badUom = () => products.CreateAsync(NewStandardProduct("WASHER-M8", 999_999L, exemptId));
        var badTaxClass = () => products.CreateAsync(NewStandardProduct("WASHER-M8", pieceId, 999_999L));

        await badUom.Should().ThrowAsync<InvalidOperationException>();
        await badTaxClass.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ADeactivatedProductCanStillBeReactivated()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);

        var productId = await products.CreateAsync(NewStandardProduct("SCREW-M6", pieceId, exemptId));

        await products.DeactivateAsync(productId);
        (await fixture.ScalarAsync($"SELECT active FROM product WHERE id = {productId};")).Should().Be("0");

        await products.ReactivateAsync(productId);
        (await fixture.ScalarAsync($"SELECT active FROM product WHERE id = {productId};")).Should().Be("1");
    }

    [Fact]
    public async Task AVariantCanBeCreatedEditedAndDeactivated()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var productId = await products.CreateAsync(NewStandardProduct("HINGE-100", pieceId, exemptId));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("HINGE-100-SS", Attrs(("finish", "Stainless")), Money.FromDecimal(4.50m)));

        (await fixture.ScalarAsync($"SELECT sku FROM product_variant WHERE id = {variantId};")).Should().Be("HINGE-100-SS");

        await products.UpdateVariantAsync(
            variantId,
            new SaveProductVariantCommand("HINGE-100-SS", Attrs(("finish", "Stainless")), Money.FromDecimal(4.75m)));

        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantId};")).Should().Be("47500");

        await products.DeactivateVariantAsync(variantId);
        (await fixture.ScalarAsync($"SELECT active FROM product_variant WHERE id = {variantId};")).Should().Be("0");

        await products.ReactivateVariantAsync(variantId);
        (await fixture.ScalarAsync($"SELECT active FROM product_variant WHERE id = {variantId};")).Should().Be("1");
    }

    [Fact]
    public async Task ADuplicateSkuIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var productId = await products.CreateAsync(NewStandardProduct("CLAMP-25", pieceId, exemptId));

        await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("CLAMP-25-A", Attrs(("size", "A")), Money.FromDecimal(3m)));

        var again = () => products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("CLAMP-25-A", Attrs(("size", "B")), Money.FromDecimal(3m)));

        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already a variant*");
    }

    [Fact]
    public async Task FR_2_4_ABoxUnitCanBeAddedToAProductAndConvertsCorrectly()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var uoms = fixture.Resolve<IUomMaintenance>();
        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box", "box", 0));

        var productId = await products.CreateAsync(NewStandardProduct("BOLT-BOXED", pieceId, exemptId));
        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("BOLT-BOXED-A", Attrs(("pack", "std")), Money.FromDecimal(0.10m)));

        var uomOptionId = await products.AddUomOptionAsync(
            productId,
            new SaveProductUomCommand(boxId, UomConversion.FromDecimal(100m), SellingPrice: null));

        (await fixture.ScalarAsync($"SELECT conversion_factor FROM product_uom WHERE id = {uomOptionId};"))
            .Should().Be("1000000", "1 box = 100 pieces is 1000000 (docs/01_DATA_MODEL.md §3)");

        // Build the domain product exactly as a later sale/report would, from the stored rows,
        // and prove selling 2 boxes converts to exactly 200 base units (AC-08).
        var domainProduct = await BuildDomainProductAsync(products, productId);
        var baseQty = UomConverter.ToBase(2m, boxId, domainProduct);
        baseQty.Value.Should().Be(200m);
        baseQty.UomId.Should().Be(pieceId);

        // The variant's own price is unaffected by the unit added to the product.
        var variant = (await products.ListVariantsAsync(productId)).Should().ContainSingle(v => v.Id == variantId).Subject;
        variant.Price.Should().Be(Money.FromDecimal(0.10m));
    }

    [Fact]
    public async Task FR_2_4_AddingTheBaseUnitAgainOrAddingItTwiceIsRefused()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var uoms = fixture.Resolve<IUomMaintenance>();
        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box", "box", 0));

        var productId = await products.CreateAsync(NewStandardProduct("BOLT-DUP", pieceId, exemptId));

        var addBaseAgain = () => products.AddUomOptionAsync(
            productId, new SaveProductUomCommand(pieceId, UomConversion.Base, SellingPrice: null));
        await addBaseAgain.Should().ThrowAsync<InvalidOperationException>().WithMessage("*base unit*");

        await products.AddUomOptionAsync(productId, new SaveProductUomCommand(boxId, UomConversion.FromDecimal(100m), null));

        var addBoxAgain = () => products.AddUomOptionAsync(
            productId, new SaveProductUomCommand(boxId, UomConversion.FromDecimal(50m), null));
        await addBoxAgain.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already sells*");
    }

    [Fact]
    public async Task FR_2_4_TheBaseUnitCannotBeEditedOrRemovedThroughTheUomOptionEndpoints()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var productId = await products.CreateAsync(NewStandardProduct("BOLT-BASE", pieceId, exemptId));

        var baseOption = (await products.ListUomOptionsAsync(productId)).Should().ContainSingle(o => o.IsBase).Subject;

        var editBase = () => products.UpdateUomOptionAsync(
            baseOption.Id, new SaveProductUomCommand(pieceId, UomConversion.Base, null));
        var removeBase = () => products.RemoveUomOptionAsync(baseOption.Id);

        await editBase.Should().ThrowAsync<InvalidOperationException>().WithMessage("*base unit*");
        await removeBase.Should().ThrowAsync<InvalidOperationException>().WithMessage("*base unit*");
    }

    [Fact]
    public async Task FR_2_4_ANonBaseUnitCanBeEditedAndRemoved()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var uoms = fixture.Resolve<IUomMaintenance>();
        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box", "box", 0));

        var productId = await products.CreateAsync(NewStandardProduct("BOLT-EDIT", pieceId, exemptId));
        var optionId = await products.AddUomOptionAsync(
            productId, new SaveProductUomCommand(boxId, UomConversion.FromDecimal(100m), null));

        await products.UpdateUomOptionAsync(
            optionId, new SaveProductUomCommand(boxId, UomConversion.FromDecimal(144m), Money.FromDecimal(999m)));

        (await fixture.ScalarAsync($"SELECT conversion_factor FROM product_uom WHERE id = {optionId};")).Should().Be("1440000");
        (await fixture.ScalarAsync($"SELECT selling_price FROM product_uom WHERE id = {optionId};")).Should().Be("9990000");

        await products.RemoveUomOptionAsync(optionId);
        (await fixture.CountAsync($"SELECT COUNT(*) FROM product_uom WHERE id = {optionId};")).Should().Be(0);
    }

    [Fact]
    public async Task FR_2_5_APriceCanBeSetPerUnitAndFallsBackToBasePriceTimesFactorWhenNotSet()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var uoms = fixture.Resolve<IUomMaintenance>();
        var boxId = await uoms.CreateAsync(new SaveUomCommand("Box", "box", 0));

        var productId = await products.CreateAsync(NewStandardProduct("BOLT-PRICED", pieceId, exemptId));
        await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("BOLT-PRICED-A", Attrs(("pack", "std")), Money.FromDecimal(10m)));

        await products.AddUomOptionAsync(
            productId, new SaveProductUomCommand(boxId, UomConversion.FromDecimal(100m), Money.FromDecimal(950m)));

        var domainProduct = await BuildDomainProductAsync(products, productId);
        var variant = (await products.ListVariantsAsync(productId)).Single();

        UomConverter.ResolvePrice(variant.Price, boxId, domainProduct).Should().Be(Money.FromDecimal(950m));
        UomConverter.ResolvePrice(variant.Price, pieceId, domainProduct).Should().Be(Money.FromDecimal(10m));
    }

    [Fact]
    public async Task FR_2_6_TheMatrixGeneratorCreatesSixtyVariantsFromTenLengthsByTwoThreadsByThreeFinishesInOneOperation()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var productId = await products.CreateAsync(NewStandardProduct("BOLT-MATRIX", pieceId, exemptId));

        var axes = new[]
        {
            new VariantAxis("length", Enumerable.Range(1, 10).Select(i => i * 10 + "mm").ToArray()),
            new VariantAxis("thread", ["M8", "M10"]),
            new VariantAxis("finish", ["Zinc", "Black", "Stainless"]),
        };

        var preview = await products.PreviewVariantMatrixAsync(productId, axes);
        preview.ToCreate.Should().HaveCount(60, "10 lengths x 2 threads x 3 finishes (SRS §2.2)");
        preview.SkippedExistingCount.Should().Be(0);

        var ids = await products.CommitVariantMatrixAsync(productId, preview.ToCreate, Money.FromDecimal(1m));

        ids.Should().HaveCount(60);
        (await fixture.CountAsync($"SELECT COUNT(*) FROM product_variant WHERE product_id = {productId};")).Should().Be(60);

        var skus = (await products.ListVariantsAsync(productId)).Select(v => v.Sku).ToList();
        skus.Should().OnlyHaveUniqueItems("every generated SKU must be distinct");
    }

    [Fact]
    public async Task FR_2_6_CommittingTheMatrixTwiceSkipsCombinationsThatAlreadyExist()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);
        var productId = await products.CreateAsync(NewStandardProduct("BOLT-MATRIX-2", pieceId, exemptId));

        var axes = new[]
        {
            new VariantAxis("size", ["S", "M", "L"]),
            new VariantAxis("colour", ["Red", "Blue"]),
        };

        var firstPreview = await products.PreviewVariantMatrixAsync(productId, axes);
        await products.CommitVariantMatrixAsync(productId, firstPreview.ToCreate, Money.FromDecimal(1m));

        (await fixture.CountAsync($"SELECT COUNT(*) FROM product_variant WHERE product_id = {productId};")).Should().Be(6);

        // Asking again for the same axes: everything already exists.
        var secondPreview = await products.PreviewVariantMatrixAsync(productId, axes);
        secondPreview.ToCreate.Should().BeEmpty();
        secondPreview.SkippedExistingCount.Should().Be(6);

        var secondIds = await products.CommitVariantMatrixAsync(productId, secondPreview.ToCreate, Money.FromDecimal(1m));
        secondIds.Should().BeEmpty();

        (await fixture.CountAsync($"SELECT COUNT(*) FROM product_variant WHERE product_id = {productId};")).Should().Be(6);
    }

    [Fact]
    public async Task EveryChangeIsRecordedInTheAuditTrail()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, pieceId, exemptId) = await SetUpAsync(fixture);

        var productId = await products.CreateAsync(NewStandardProduct("BOLT-AUDIT", pieceId, exemptId));
        await products.UpdateAsync(productId, NewStandardProduct("BOLT-AUDIT", pieceId, exemptId, name: "Renamed bolt"));
        await products.DeactivateAsync(productId);
        await products.ReactivateAsync(productId);

        var ownerId = fixture.Resolve<ISession>().CurrentUser!.Id;

        foreach (var action in new[]
                 {
                     CatalogueAuditActions.Created,
                     CatalogueAuditActions.Updated,
                     CatalogueAuditActions.Deactivated,
                     CatalogueAuditActions.Reactivated,
                 })
        {
            (await fixture.CountAsync(
                $"SELECT COUNT(*) FROM audit_log WHERE action = '{action}' "
                + $"AND entity_type = '{CatalogueAuditActions.ProductEntityType}' "
                + $"AND user_id = {ownerId} AND entity_id = {productId};"))
                .Should().Be(1, "{0} is recorded against the owner who did it", action);
        }
    }

    private static Dictionary<string, string> Attrs(params (string Key, string Value)[] pairs)
    {
        var dictionary = new Dictionary<string, string>();

        foreach (var (key, value) in pairs)
        {
            dictionary[key] = value;
        }

        return dictionary;
    }

    private static async Task<(IProductMaintenance Products, long PieceUomId, long ExemptTaxClassId)> SetUpAsync(SaleFixture fixture)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        return (products, pieceId, exemptId);
    }

    private static SaveProductCommand NewStandardProduct(string code, long baseUomId, long taxClassId, string? name = null) =>
        new(
            code,
            name ?? code,
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            baseUomId,
            ProductType.Standard,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null);

    private static async Task<Product> BuildDomainProductAsync(IProductMaintenance products, long productId)
    {
        var record = await products.FindByIdAsync(productId) ?? throw new InvalidOperationException("product not found");
        var options = await products.ListUomOptionsAsync(productId);

        var uomOptions = options
            .Select(option => new ProductUomOption(
                option.UomId,
                option.UomSymbol,
                DecimalPlaces: option.IsBase ? 0 : 0,
                option.Conversion,
                option.IsBase,
                option.SellingPrice))
            .ToList();

        return new Product(record.Id, record.Code, record.Name, record.Type, record.BaseUomId, uomOptions);
    }
}
