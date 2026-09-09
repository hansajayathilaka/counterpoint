using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SchemaProduct = Counterpoint.Infrastructure.Data.Schema.Product;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// A price at or below cost warns and requires confirmation, and every actual price change on an
/// existing variant is logged to <c>price_change_log</c> (SRS FR-2.17, FR-2.18, task P1-T08 step
/// 5, done-when: "Setting a price below cost warns and requires confirmation, and is logged").
/// </summary>
public sealed class PriceChangeLogTests
{
    [Fact]
    public async Task FR_2_18_APriceAtOrBelowCostThrowsAWarningAndWritesNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, _, variantId, variant) = await SetUpVariantWithCostAsync(fixture, cost: 5.00m, initialPrice: 8.00m);

        var atCost = () => products.UpdateVariantAsync(
            variantId, new SaveProductVariantCommand(variant.Sku, variant.Attributes, Money.FromDecimal(5.00m)));
        var belowCost = () => products.UpdateVariantAsync(
            variantId, new SaveProductVariantCommand(variant.Sku, variant.Attributes, Money.FromDecimal(4.99m)));

        var atCostException = await atCost.Should().ThrowAsync<PriceBelowCostWarningException>();
        atCostException.Which.Price.Should().Be(Money.FromDecimal(5.00m));
        atCostException.Which.Cost.Should().Be(Money.FromDecimal(5.00m));

        await belowCost.Should().ThrowAsync<PriceBelowCostWarningException>();

        (await fixture.CountAsync($"SELECT COUNT(*) FROM price_change_log WHERE product_variant_id = {variantId};"))
            .Should().Be(0, "a refused save must write nothing");
        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantId};"))
            .Should().Be("80000", "the price on record is untouched by the refused attempts");
    }

    [Fact]
    public async Task FR_2_18_ConfirmingTheWarningSavesThePriceAndFR_2_17_LogsTheChange()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, _, variantId, variant) = await SetUpVariantWithCostAsync(fixture, cost: 5.00m, initialPrice: 8.00m);
        var userId = fixture.Resolve<ISession>().CurrentUser!.Id;

        await products.UpdateVariantAsync(
            variantId,
            new SaveProductVariantCommand(
                variant.Sku, variant.Attributes, Money.FromDecimal(4.50m), ConfirmBelowCost: true, Reason: "Clearing old stock."));

        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantId};")).Should().Be("45000");

        var history = await products.GetPriceHistoryAsync(variantId);
        history.Should().ContainSingle();
        history[0].OldPrice.Should().Be(Money.FromDecimal(8.00m));
        history[0].NewPrice.Should().Be(Money.FromDecimal(4.50m));
        history[0].UserId.Should().Be(userId);
        history[0].Reason.Should().Be("Clearing old stock.");

        (await fixture.CountAsync($"SELECT COUNT(*) FROM price_change_log WHERE product_variant_id = {variantId};"))
            .Should().Be(1);
    }

    [Fact]
    public async Task FR_2_17_APriceChangeAboveCostIsLoggedWithoutNeedingConfirmation()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, _, variantId, variant) = await SetUpVariantWithCostAsync(fixture, cost: 5.00m, initialPrice: 8.00m);

        await products.UpdateVariantAsync(
            variantId, new SaveProductVariantCommand(variant.Sku, variant.Attributes, Money.FromDecimal(9.00m)));

        var history = await products.GetPriceHistoryAsync(variantId);
        history.Should().ContainSingle();
        history[0].OldPrice.Should().Be(Money.FromDecimal(8.00m));
        history[0].NewPrice.Should().Be(Money.FromDecimal(9.00m));
    }

    [Fact]
    public async Task ReSavingTheSamePriceWritesNoPriceChangeLogRow()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (products, _, variantId, variant) = await SetUpVariantWithCostAsync(fixture, cost: 5.00m, initialPrice: 8.00m);

        await products.UpdateVariantAsync(
            variantId, new SaveProductVariantCommand(variant.Sku, variant.Attributes, Money.FromDecimal(8.00m)));

        (await fixture.CountAsync($"SELECT COUNT(*) FROM price_change_log WHERE product_variant_id = {variantId};"))
            .Should().Be(0, "no price actually changed, so there is nothing to log");
    }

    [Fact]
    public async Task FR_2_18_CreatingANewVariantAtOrBelowAnExistingProductCostAlsoWarns()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var products = fixture.Resolve<IProductMaintenance>();
        var (uomId, taxClassId) = await ReferenceDataAsync(fixture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            "COST-GUARD-1", "Cost guard product", null, null, null, uomId,
            ProductType.Standard, taxClassId, null, false, null, null, null));

        await SetCostAsync(fixture, productId, Money.FromDecimal(6.00m));

        var createAtCost = () => products.CreateVariantAsync(
            productId, new SaveProductVariantCommand("COST-GUARD-1-A", CatalogueAttributes.Empty, Money.FromDecimal(6.00m)));

        await createAtCost.Should().ThrowAsync<PriceBelowCostWarningException>();

        var createConfirmed = () => products.CreateVariantAsync(
            productId, new SaveProductVariantCommand("COST-GUARD-1-A", CatalogueAttributes.Empty, Money.FromDecimal(6.00m), ConfirmBelowCost: true));

        await createConfirmed.Should().NotThrowAsync();
    }

    private static async Task<(IProductMaintenance Products, long ProductId, long VariantId, ProductVariantRecord Variant)> SetUpVariantWithCostAsync(
        SaleFixture fixture, decimal cost, decimal initialPrice)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var (uomId, taxClassId) = await ReferenceDataAsync(fixture);

        var productId = await products.CreateAsync(new SaveProductCommand(
            "PRICE-LOG-1", "Price log product", null, null, null, uomId,
            ProductType.Standard, taxClassId, null, false, null, null, null));

        var variantId = await products.CreateVariantAsync(
            productId, new SaveProductVariantCommand("PRICE-LOG-1-A", CatalogueAttributes.Empty, Money.FromDecimal(initialPrice)));

        await SetCostAsync(fixture, productId, Money.FromDecimal(cost));

        var variant = (await products.ListVariantsAsync(productId)).Single(v => v.Id == variantId);

        return (products, productId, variantId, variant);
    }

    private static async Task<(long UomId, long TaxClassId)> ReferenceDataAsync(SaleFixture fixture)
    {
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        return (pieceId, exemptId);
    }

    /// <summary>
    /// Sets <c>product.cost_avg</c> directly, the same way <c>CatalogueTestProducts</c> plants a
    /// reference row: the cost the below-cost warning compares against is normally built up by
    /// stock receipts (P1-T07), which is out of scope for a pricing test to re-derive.
    /// </summary>
    private static async Task SetCostAsync(SaleFixture fixture, long productId, Money cost)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();
            var product = await context.Set<SchemaProduct>().FirstAsync(p => p.Id == productId, token);
            product.CostAvg = cost;
            await context.SaveChangesAsync(token);
        });
    }

    private static class CatalogueAttributes
    {
        internal static readonly System.Collections.Generic.Dictionary<string, string> Empty = [];
    }
}
