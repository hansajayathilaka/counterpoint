using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// Seeds one sellable variant at a chosen shelf price and tax rate, with stock on hand, and
/// switches the shop's pricing mode - the two inputs every tax and discount identity depends on.
/// The same entity-level technique <c>TaxedSaleTests</c> uses.
/// </summary>
internal static class PricedVariantSeeder
{
    private static readonly DateTimeOffset SeededAt = new(2026, 9, 1, 8, 0, 0, TimeSpan.FromHours(5.5));

    internal static Task<long> SeedAsync(SaleFixture fixture, string sku, decimal price, decimal taxPercent)
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();
        var ledger = fixture.Resolve<IStockLedger>();

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var uomId = await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var userId = await context.Set<AppUser>().Select(row => row.Id).FirstAsync(token);

            var taxClass = new TaxClass { Name = "Rate " + sku, Rate = TaxRate.FromPercent(taxPercent), Active = true };
            context.Add(taxClass);
            await context.SaveChangesAsync(token);

            var product = new Product
            {
                Code = sku,
                Name = "Item " + sku,
                NameAlt = null,
                CategoryId = null,
                BrandId = null,
                BaseUomId = uomId,
                Type = "STANDARD",
                TaxClassId = taxClass.Id,
                CostAvg = Money.FromDecimal(10m),
                ReorderLevel = 0,
                ReorderQty = 0,
                Location = "A1",
                NonReturnable = false,
                MinSellQty = 0,
                MaxDiscountRate = null,
                WarrantyDays = null,
                Notes = null,
                ImagePath = null,
                Active = true,
                CreatedAt = SeededAt,
                UpdatedAt = SeededAt,
            };
            context.Add(product);
            await context.SaveChangesAsync(token);

            context.Add(new ProductUom
            {
                ProductId = product.Id,
                UomId = uomId,
                ConversionFactor = UomConversion.Base.ToScaled(),
                SellingPrice = null,
                IsBase = true,
            });
            await context.SaveChangesAsync(token);

            var variant = new ProductVariant
            {
                ProductId = product.Id,
                Sku = sku + "-A",
                Attributes = "{}",
                Price = Money.FromDecimal(price),
                Active = true,
                CreatedAt = SeededAt,
            };
            context.Add(variant);
            await context.SaveChangesAsync(token);

            await ledger.PostAsync(
                new StockPosting(
                    variant.Id, "OPENING", Quantity.FromDecimal(100m, uomId), Money.FromDecimal(10m),
                    "OPENING", RefDocId: null, userId, SeededAt),
                token);

            return variant.Id;
        });
    }

    internal static Task UsePricingModeAsync(SaleFixture fixture, bool pricesIncludeTax) =>
        fixture.Resolve<ISettings>().UpdateAsync(current => current with
        {
            Tax = current.Tax with { PricesIncludeTax = pricesIncludeTax },
        });
}
