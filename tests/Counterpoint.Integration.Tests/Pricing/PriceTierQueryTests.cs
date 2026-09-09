using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchemaPriceTier = Counterpoint.Infrastructure.Data.Schema.PriceTier;

namespace Counterpoint.Integration.Tests.Pricing;

/// <summary>
/// <c>price_tier</c> read for <see cref="PriceResolver"/> through a real SQLite file (SRS
/// FR-2.14-FR-2.16, task P1-T08). The rows are written directly, the way P5-T01's maintenance
/// screen will (docs/01_DATA_MODEL.md §3) - this is the read side, exercised end to end into the
/// same resolver the precedence tests use.
/// </summary>
public sealed class PriceTierQueryTests
{
    [Fact]
    public async Task FR_2_14_ToFR_2_16_APriceTierRowWrittenToTheDatabaseFeedsThePriceResolverThroughItsPrecedence()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        var productId = await products.CreateAsync(new SaveProductCommand(
            "TIER-1", "Tiered product", null, null, null, pieceId, ProductType.Standard, exemptId, null, false, null, null, null));
        var variantId = await products.CreateVariantAsync(
            productId, new SaveProductVariantCommand("TIER-1-A", new System.Collections.Generic.Dictionary<string, string>(), Money.FromDecimal(10m)));

        await WriteTierRowAsync(fixture, variantId, "TRADE", minQty: 0m, price: 7.5m, validFrom: null, validTo: null);
        await WriteTierRowAsync(fixture, variantId, "TRADE", minQty: 10m, price: 6.5m, validFrom: null, validTo: null);

        var tierQuery = fixture.Resolve<IPriceTierQuery>();
        var candidates = await tierQuery.ListForVariantAsync(variantId);

        candidates.Should().HaveCount(2);
        candidates.Should().OnlyContain(c => c.MinQty.UomId == pieceId, "min_qty is read back in the product's base unit");

        var product = await BuildDomainProductAsync(products, productId);
        var asOf = new DateOnly(2026, 9, 8);

        // Below the quantity break: the flat trade tier price wins (level 3).
        var belowBreak = PriceResolver.Resolve(
            product, Money.FromDecimal(10m), pieceId, Quantity.FromDecimal(5m, pieceId),
            CustomerPriceTier.Trade, candidates, asOf);
        belowBreak.Basis.Should().Be(PriceBasis.Tier);
        belowBreak.Price.Should().Be(Money.FromDecimal(7.5m));

        // At the quantity break: the break itself wins (level 2).
        var atBreak = PriceResolver.Resolve(
            product, Money.FromDecimal(10m), pieceId, Quantity.FromDecimal(10m, pieceId),
            CustomerPriceTier.Trade, candidates, asOf);
        atBreak.Basis.Should().Be(PriceBasis.QuantityBreak);
        atBreak.Price.Should().Be(Money.FromDecimal(6.5m));

        // A retail sale of the same variant sees neither row - both are trade-only.
        var retail = PriceResolver.Resolve(
            product, Money.FromDecimal(10m), pieceId, Quantity.FromDecimal(20m, pieceId),
            CustomerPriceTier.Retail, candidates, asOf);
        retail.Basis.Should().Be(PriceBasis.BaseVariantPrice);
    }

    [Fact]
    public async Task AVariantWithNoPriceTierRowsReturnsAnEmptyList()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        var productId = await products.CreateAsync(new SaveProductCommand(
            "TIER-2", "Untiered product", null, null, null, pieceId, ProductType.Standard, exemptId, null, false, null, null, null));
        var variantId = await products.CreateVariantAsync(
            productId, new SaveProductVariantCommand("TIER-2-A", new System.Collections.Generic.Dictionary<string, string>(), Money.FromDecimal(10m)));

        var candidates = await fixture.Resolve<IPriceTierQuery>().ListForVariantAsync(variantId);

        candidates.Should().BeEmpty();
    }

    private static async Task<Product> BuildDomainProductAsync(IProductMaintenance products, long productId)
    {
        var record = await products.FindByIdAsync(productId) ?? throw new InvalidOperationException("product not found");
        var options = await products.ListUomOptionsAsync(productId);

        var uomOptions = options
            .Select(option => new ProductUomOption(option.UomId, option.UomSymbol, 0, option.Conversion, option.IsBase, option.SellingPrice))
            .ToList();

        return new Product(record.Id, record.Code, record.Name, record.Type, record.BaseUomId, uomOptions);
    }

    private static async Task WriteTierRowAsync(
        SaleFixture fixture, long variantId, string tier, decimal minQty, decimal price, DateOnly? validFrom, DateOnly? validTo)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            context.Add(new SchemaPriceTier
            {
                ProductVariantId = variantId,
                Tier = tier,
                MinQty = Quantity.FromDecimal(minQty, 0).ToScaled(),
                Price = Money.FromDecimal(price),
                ValidFrom = validFrom?.ToString("yyyy-MM-dd"),
                ValidTo = validTo?.ToString("yyyy-MM-dd"),
            });

            await context.SaveChangesAsync(token);
        });
    }
}
