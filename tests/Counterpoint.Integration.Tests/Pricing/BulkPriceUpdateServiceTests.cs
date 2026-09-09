using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Pricing;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Integration.Tests.Sales;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Pricing;

/// <summary>
/// Bulk price update by category, brand or supplier, with a preview before applying (SRS
/// FR-2.19, task P1-T08 step 6).
/// </summary>
public sealed class BulkPriceUpdateServiceTests
{
    [Fact]
    public async Task FR_2_19_PreviewComputesTheNewPricesWithoutWritingAnything()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (categoryId, _, _) = await ReferenceDataAsync(fixture);
        var productId = await CreateProductAsync(fixture, "BULK-1", categoryId);
        var variantId = await CreateVariantAsync(fixture, productId, "BULK-1-A", Money.FromDecimal(100m));

        var bulk = fixture.Resolve<IBulkPriceUpdateService>();
        var preview = await bulk.PreviewAsync(new BulkPriceUpdateRequest(
            new BulkPriceQueryFilter(categoryId, null, null),
            PriceAdjustment.ByPercentage(Percentage.FromPercent(10m))));

        preview.Count.Should().Be(1);
        preview.Lines[0].ProductVariantId.Should().Be(variantId);
        preview.Lines[0].OldPrice.Should().Be(Money.FromDecimal(100m));
        preview.Lines[0].NewPrice.Should().Be(Money.FromDecimal(110m));

        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantId};"))
            .Should().Be("1000000", "a preview must write nothing");
    }

    [Fact]
    public async Task FR_2_19_ApplyingUpdatesEveryMatchingVariantAndLogsOneRowEachPlusOneAuditRow()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (categoryId, _, _) = await ReferenceDataAsync(fixture);
        var productId = await CreateProductAsync(fixture, "BULK-2", categoryId);
        var variantAId = await CreateVariantAsync(fixture, productId, "BULK-2-A", Money.FromDecimal(100m));
        var variantBId = await CreateVariantAsync(fixture, productId, "BULK-2-B", Money.FromDecimal(50m));

        var bulk = fixture.Resolve<IBulkPriceUpdateService>();
        var request = new BulkPriceUpdateRequest(
            new BulkPriceQueryFilter(categoryId, null, null),
            PriceAdjustment.ByPercentage(Percentage.FromPercent(10m)),
            Reason: "Supplier cost increase.");

        var changed = await bulk.ApplyAsync(request);

        changed.Should().Be(2);
        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantAId};")).Should().Be("1100000");
        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantBId};")).Should().Be("550000");

        (await fixture.CountAsync("SELECT COUNT(*) FROM price_change_log;")).Should().Be(2, "one price_change_log row per variant changed");
        (await fixture.CountAsync(
            $"SELECT COUNT(*) FROM audit_log WHERE action = '{PricingAuditActions.BulkPriceUpdateApplied}';"))
            .Should().Be(1, "one audit row for the whole update, not one per variant");

        var payload = await fixture.ScalarAsync(
            $"SELECT after_json FROM audit_log WHERE action = '{PricingAuditActions.BulkPriceUpdateApplied}';");
        payload.Should().Contain("\"variant_count\":2");

        (await fixture.ScalarAsync("SELECT reason FROM audit_log WHERE action = '" + PricingAuditActions.BulkPriceUpdateApplied + "';"))
            .Should().Be("Supplier cost increase.");
    }

    [Fact]
    public async Task FR_2_19_TheFilterOnlyTouchesProductsInTheNamedCategory()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (categoryId, otherCategoryId, _) = await ReferenceDataAsync(fixture);

        var inCategoryProductId = await CreateProductAsync(fixture, "BULK-3", categoryId, name: "Coach bolt");
        var inCategoryVariantId = await CreateVariantAsync(fixture, inCategoryProductId, "BULK-3-A", Money.FromDecimal(100m));

        var outOfCategoryProductId = await CreateProductAsync(fixture, "BULK-4", otherCategoryId, name: "Treated plank");
        var outOfCategoryVariantId = await CreateVariantAsync(fixture, outOfCategoryProductId, "BULK-4-A", Money.FromDecimal(100m));

        var bulk = fixture.Resolve<IBulkPriceUpdateService>();
        await bulk.ApplyAsync(new BulkPriceUpdateRequest(
            new BulkPriceQueryFilter(categoryId, null, null),
            PriceAdjustment.ByPercentage(Percentage.FromPercent(10m))));

        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {inCategoryVariantId};")).Should().Be("1100000");
        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {outOfCategoryVariantId};"))
            .Should().Be("1000000", "a product filed under a different category is untouched");
    }

    [Fact]
    public async Task FR_2_18_ApplyingAnUpdateThatWouldGoAtOrBelowCostIsRefusedThenSucceedsWithConfirmation()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (categoryId, _, _) = await ReferenceDataAsync(fixture);
        var productId = await CreateProductAsync(fixture, "BULK-5", categoryId);
        var variantId = await CreateVariantAsync(fixture, productId, "BULK-5-A", Money.FromDecimal(10m));
        await SetCostAsync(fixture, productId, Money.FromDecimal(9m));

        var bulk = fixture.Resolve<IBulkPriceUpdateService>();
        var request = new BulkPriceUpdateRequest(
            new BulkPriceQueryFilter(categoryId, null, null),
            PriceAdjustment.ByPercentage(Percentage.FromPercent(-10m)));

        var apply = () => bulk.ApplyAsync(request);
        var exception = await apply.Should().ThrowAsync<BulkPriceBelowCostWarningException>();
        exception.Which.Lines.Should().ContainSingle(line => line.ProductVariantId == variantId);

        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantId};"))
            .Should().Be("100000", "the refused attempt writes nothing");

        var confirmed = await bulk.ApplyAsync(request with { ConfirmBelowCost = true });
        confirmed.Should().Be(1);
        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantId};")).Should().Be("90000");
    }

    [Fact]
    public async Task ASteepEnoughDecreaseThatWouldGoNegativeIsRefusedAndWritesNothing()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        var (categoryId, _, _) = await ReferenceDataAsync(fixture);
        var productId = await CreateProductAsync(fixture, "BULK-6", categoryId);
        var variantId = await CreateVariantAsync(fixture, productId, "BULK-6-A", Money.FromDecimal(10m));

        var bulk = fixture.Resolve<IBulkPriceUpdateService>();
        var apply = () => bulk.ApplyAsync(new BulkPriceUpdateRequest(
            new BulkPriceQueryFilter(categoryId, null, null),
            PriceAdjustment.ByFixedAmount(Money.FromDecimal(-20m)),
            ConfirmBelowCost: true));

        await apply.Should().ThrowAsync<InvalidOperationException>();

        (await fixture.ScalarAsync($"SELECT price FROM product_variant WHERE id = {variantId};")).Should().Be("100000");
    }

    [Fact]
    public async Task AC_17_ACashierIsRefusedByTheServiceItself()
    {
        await using var fixture = await SaleFixture.CreateSignedInAsync();
        await fixture.Resolve<IUserAdministration>().CreateAsync(
            new CreateUserCommand("priya", "Priya", "counter1", Role.Cashier));

        var authentication = fixture.Resolve<IAuthenticationService>();
        await authentication.LogOutAsync();
        (await authentication.LogInAsync("priya", "counter1")).Succeeded.Should().BeTrue();

        var bulk = fixture.Resolve<IBulkPriceUpdateService>();
        var preview = () => bulk.PreviewAsync(new BulkPriceUpdateRequest(
            new BulkPriceQueryFilter(null, null, null), PriceAdjustment.ByPercentage(Percentage.FromPercent(1m))));

        await preview.Should().ThrowAsync<NotAuthorisedException>();
    }

    private static async Task<(long CategoryId, long OtherCategoryId, long TaxClassId)> ReferenceDataAsync(SaleFixture fixture)
    {
        var categories = fixture.Resolve<ICategoryMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var categoryId = await categories.CreateAsync(new SaveCategoryCommand("Fixings", null));
        var otherCategoryId = await categories.CreateAsync(new SaveCategoryCommand("Timber", null));
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        return (categoryId, otherCategoryId, exemptId);
    }

    private static async Task<long> CreateProductAsync(SaleFixture fixture, string code, long categoryId, string? name = null)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        var uoms = fixture.Resolve<IUomMaintenance>();
        var taxClasses = fixture.Resolve<ITaxClassMaintenance>();

        var pieceId = (await uoms.ListAsync()).Single(u => u.Name == "Piece").Id;
        var exemptId = (await taxClasses.ListAsync()).Single(t => t.Name == "Exempt").Id;

        return await products.CreateAsync(new SaveProductCommand(
            code, name ?? code, null, categoryId, null, pieceId, ProductType.Standard, exemptId, null, false, null, null, null));
    }

    private static async Task<long> CreateVariantAsync(SaleFixture fixture, long productId, string sku, Money price)
    {
        var products = fixture.Resolve<IProductMaintenance>();
        return await products.CreateVariantAsync(productId, new SaveProductVariantCommand(sku, EmptyAttributes, price));
    }

    private static async Task SetCostAsync(SaleFixture fixture, long productId, Money cost)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();
            var product = await context.Set<Counterpoint.Infrastructure.Data.Schema.Product>().FirstAsync(p => p.Id == productId, token);
            product.CostAvg = cost;
            await context.SaveChangesAsync(token);
        });
    }

    private static readonly System.Collections.Generic.Dictionary<string, string> EmptyAttributes = [];
}
