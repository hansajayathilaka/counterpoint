using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// Finds the variants a bulk price update touches (docs/01_DATA_MODEL.md §3, SRS FR-2.19).
/// </summary>
internal sealed class SqlitePriceQuery : IPriceQuery
{
    private readonly SqliteUnitOfWork _unitOfWork;

    public SqlitePriceQuery(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PriceQueryVariant>> FindVariantsAsync(
        BulkPriceQueryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _unitOfWork.ExecuteInTransactionAsync(
            async (_, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var products = context.Set<Product>().Where(product => product.Active);

                if (filter.CategoryId is { } categoryId)
                {
                    products = products.Where(product => product.CategoryId == categoryId);
                }

                if (filter.BrandId is { } brandId)
                {
                    products = products.Where(product => product.BrandId == brandId);
                }

                if (filter.SupplierId is { } supplierId)
                {
                    var supplierProductIds = context.Set<ProductSupplier>()
                        .Where(link => link.SupplierId == supplierId)
                        .Select(link => link.ProductId);

                    products = products.Where(product => supplierProductIds.Contains(product.Id));
                }

                var rows = await (
                    from product in products
                    join variant in context.Set<ProductVariant>() on product.Id equals variant.ProductId
                    where variant.Active
                    select new
                    {
                        variant.Id,
                        product.Name,
                        variant.Sku,
                        variant.Price,
                        product.CostAvg,
                    })
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                IReadOnlyList<PriceQueryVariant> result =
                    [.. rows.Select(row => new PriceQueryVariant(row.Id, row.Name, row.Sku, row.Price, row.CostAvg))];

                return result;
            },
            cancellationToken);
    }
}
