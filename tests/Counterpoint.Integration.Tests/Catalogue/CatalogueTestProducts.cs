using System;
using System.Linq;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Counterpoint.Integration.Tests.Sales;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Integration.Tests.Catalogue;

/// <summary>
/// Puts a product on the books that references the given category, brand, unit, tax class and/or
/// supplier, so a delete-guard test has something to be refused by (FR-2.20, FR-2.21, FR-6).
/// </summary>
internal static class CatalogueTestProducts
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5));

    internal static Task<long> CreateAsync(
        SaleFixture fixture,
        long? categoryId = null,
        long? brandId = null,
        long? uomId = null,
        long? taxClassId = null,
        long? supplierId = null,
        string code = "REF-001")
    {
        var unitOfWork = fixture.Resolve<SqliteUnitOfWork>();

        return unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            using var context = unitOfWork.CreateDbContext();

            var resolvedUomId = uomId ?? await context.Set<Uom>().Select(row => row.Id).FirstAsync(token);
            var resolvedTaxClassId = taxClassId
                ?? await context.Set<TaxClass>().Select(row => row.Id).FirstAsync(token);

            var product = new Product
            {
                Code = code,
                Name = "Reference-data test product",
                NameAlt = null,
                CategoryId = categoryId,
                BrandId = brandId,
                BaseUomId = resolvedUomId,
                Type = "STANDARD",
                TaxClassId = resolvedTaxClassId,
                CostAvg = Money.FromDecimal(1.00m),
                ReorderLevel = 0,
                ReorderQty = 0,
                Location = null,
                NonReturnable = false,
                MinSellQty = 0,
                MaxDiscountRate = null,
                WarrantyDays = null,
                Notes = null,
                ImagePath = null,
                Active = true,
                CreatedAt = CreatedAt,
                UpdatedAt = CreatedAt,
            };

            context.Add(product);
            await context.SaveChangesAsync(token);

            if (supplierId is { } sid)
            {
                context.Add(new ProductSupplier
                {
                    ProductId = product.Id,
                    SupplierId = sid,
                    SupplierRef = null,
                    LastCost = null,
                });
                await context.SaveChangesAsync(token);
            }

            return product.Id;
        });
    }
}
