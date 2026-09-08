using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// Counter search: name fragment, code, SKU, brand, category and rack location, ranked with exact
/// code matches first (SRS FR-2.11, NFR-P2).
/// </summary>
public sealed class ProductSearchServiceTests
{
    [Fact]
    public async Task FR_2_11_SearchFindsByNameCodeSkuBrandCategoryAndLocation()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var brands = fixture.Resolve<IBrandMaintenance>();
        var categories = fixture.Resolve<ICategoryMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var search = fixture.Resolve<IProductSearchService>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;
        var brandId = await brands.CreateAsync(new SaveBrandCommand("Stanley"));
        var categoryId = await categories.CreateAsync(new SaveCategoryCommand("Hand Tools", ParentId: null));

        var productId = await products.CreateAsync(new SaveProductCommand(
            "HAMMER-16OZ",
            "Claw Hammer 16oz",
            NameAlt: null,
            categoryId,
            brandId,
            pieceId,
            Domain.Catalogue.ProductType.Standard,
            exemptId,
            Location: "C4",
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null));

        var variantId = await products.CreateVariantAsync(
            productId,
            new SaveProductVariantCommand("HAMMER-16OZ-A", new Dictionary<string, string>(), Money.FromDecimal(24.50m)));

        await AssertFindsTheHammerAsync(search, variantId, "hammer", "name fragment");
        await AssertFindsTheHammerAsync(search, variantId, "HAMMER-16OZ", "product code");
        await AssertFindsTheHammerAsync(search, variantId, "HAMMER-16OZ-A", "SKU");
        await AssertFindsTheHammerAsync(search, variantId, "stanley", "brand");
        await AssertFindsTheHammerAsync(search, variantId, "hand tools", "category");
        await AssertFindsTheHammerAsync(search, variantId, "C4", "rack location");
    }

    [Fact]
    public async Task FR_2_11_AnExactCodeMatchRanksFirst()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var search = fixture.Resolve<IProductSearchService>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        // Two products share the word "spanner"; only one has the code searched for.
        var wrenchProductId = await products.CreateAsync(NewProduct("SPANNER-SET", "Spanner Set 8pc", pieceId, exemptId));
        var wrenchVariantId = await products.CreateVariantAsync(
            wrenchProductId, new SaveProductVariantCommand("SPANNER-SET-A", new Dictionary<string, string>(), Money.FromDecimal(40m)));

        var singleProductId = await products.CreateAsync(NewProduct("SPANNER-10MM", "Spanner 10mm Single", pieceId, exemptId));
        await products.CreateVariantAsync(
            singleProductId, new SaveProductVariantCommand("SPANNER-10MM-A", new Dictionary<string, string>(), Money.FromDecimal(6m)));

        var results = await search.SearchAsync("SPANNER-SET");

        results.Should().NotBeEmpty();
        results[0].ProductVariantId.Should().Be(wrenchVariantId);
        results[0].IsExactCodeMatch.Should().BeTrue();
    }

    [Fact]
    public async Task ResultsAreCappedAtFifty()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var search = fixture.Resolve<IProductSearchService>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        for (var i = 0; i < 60; i++)
        {
            var code = $"WIDGET-{i:000}";
            var productId = await products.CreateAsync(NewProduct(code, "Common Widget", pieceId, exemptId));
            await products.CreateVariantAsync(
                productId, new SaveProductVariantCommand(code + "-A", new Dictionary<string, string>(), Money.FromDecimal(1m)));
        }

        var results = await search.SearchAsync("widget");

        results.Should().HaveCountLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task ABlankQueryReturnsNoResultsRatherThanEveryProduct()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var search = fixture.Resolve<IProductSearchService>();

        var results = await search.SearchAsync("   ");

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ReindexSearchCommandRebuildsTheIndexAfterItIsClearedByHand()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();
        var search = fixture.Resolve<IProductSearchService>();
        var reindex = fixture.Resolve<IReindexSearchCommand>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        var productId = await products.CreateAsync(NewProduct("PLIERS-8IN", "Combination Pliers 8in", pieceId, exemptId));
        await products.CreateVariantAsync(
            productId, new SaveProductVariantCommand("PLIERS-8IN-A", new Dictionary<string, string>(), Money.FromDecimal(12m)));

        (await search.SearchAsync("pliers")).Should().NotBeEmpty();

        // Clears the index by hand, the same 'delete-all' special command
        // IReindexSearchCommand itself uses to start from nothing - simulating the one
        // documented way product_search can go stale (docs/01_DATA_MODEL.md §3).
        var unitOfWork = fixture.Resolve<Counterpoint.Infrastructure.Data.SqliteUnitOfWork>();
        await unitOfWork.ExecuteInTransactionAsync<object?>(async (connection, transaction, token) =>
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO product_search(product_search) VALUES('delete-all');";
            await command.ExecuteNonQueryAsync(token);
            return null;
        });

        (await search.SearchAsync("pliers")).Should().BeEmpty("the index was just cleared by hand");

        var reindexed = await reindex.ExecuteAsync();

        reindexed.Should().BeGreaterThan(0);
        (await search.SearchAsync("pliers")).Should().NotBeEmpty("the reindex command rebuilt it");
    }

    private static async Task AssertFindsTheHammerAsync(
        IProductSearchService search,
        long expectedVariantId,
        string query,
        string dimension)
    {
        var results = await search.SearchAsync(query);

        results.Should().Contain(
            result => result.ProductVariantId == expectedVariantId,
            "search must find the item by {0} ('{1}')", dimension, query);
    }

    // ConfirmDuplicate: true throughout - this file is about search ranking, not FR-2.24, and
    // ResultsAreCappedAtFifty deliberately creates many products sharing one name.
    private static SaveProductCommand NewProduct(string code, string name, long baseUomId, long taxClassId) =>
        new(
            code,
            name,
            NameAlt: null,
            CategoryId: null,
            BrandId: null,
            baseUomId,
            Domain.Catalogue.ProductType.Standard,
            taxClassId,
            Location: null,
            NonReturnable: false,
            WarrantyDays: null,
            Notes: null,
            MaxDiscountRate: null,
            ConfirmDuplicate: true);
}
