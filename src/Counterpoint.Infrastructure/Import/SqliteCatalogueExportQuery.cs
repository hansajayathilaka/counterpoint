using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Import;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Data.Schema;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Import;

/// <summary>
/// Reads the full catalogue for export, in <see cref="CatalogueExportRow"/>'s shape
/// (docs/01_DATA_MODEL.md §3, §4, SRS FR-2.23).
/// </summary>
/// <remarks>
/// A handful of bulk reads joined in memory, not one query per product: the same reasoning
/// <c>SqlitePriceQuery</c> follows for a bulk price update, applied to every active product
/// instead of a filtered set. The balance projection is read with hand-written SQL, the same as
/// <c>SqliteStockPositionReader</c> - the EF entity for that table is reserved for the ledger's
/// own machinery (CLAUDE.md invariant 3, <c>ArchitectureTests.OnlyTheStockLedgerFamilyWritesTheStockProjectionOrTheLedger</c>).
/// </remarks>
internal sealed class SqliteCatalogueExportQuery : ICatalogueExportQuery
{
    private const string BalancesSql =
        """
        SELECT product_variant_id AS ProductVariantId, qty_base AS QtyBase, cost_avg AS CostAvg
          FROM stock_balance
         WHERE product_variant_id IN @VariantIds;
        """;

    private readonly SqliteUnitOfWork _unitOfWork;

    public SqliteCatalogueExportQuery(SqliteUnitOfWork unitOfWork)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogueExportRow>> ListAllAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.ExecuteInTransactionAsync(
            async (connection, _, token) =>
            {
                using var context = _unitOfWork.CreateDbContext();

                var products = await context.Set<Product>().Where(product => product.Active).ToListAsync(token)
                    .ConfigureAwait(false);

                // Only a product's one defining variant round trips through a flat spreadsheet
                // row (docs/03_PHASE_1_core_trading.md P1-T13's scope, ICatalogueImportService's
                // remarks) - a product with more than one variant was built through the variant
                // matrix (P1-T05) and is simply not part of this export's shape. Grouped
                // client-side: SQLite's EF provider cannot translate a GroupBy().Count() filter
                // followed by a SelectMany back to rows.
                var activeVariants = await context.Set<ProductVariant>().Where(variant => variant.Active).ToListAsync(token)
                    .ConfigureAwait(false);

                var singleVariantByProductId = activeVariants
                    .GroupBy(variant => variant.ProductId)
                    .Where(group => group.Count() == 1)
                    .ToDictionary(group => group.Key, group => group.Single());

                var categoriesById = await context.Set<Category>().ToDictionaryAsync(category => category.Id, token)
                    .ConfigureAwait(false);
                var brandsById = await context.Set<Brand>().ToDictionaryAsync(brand => brand.Id, token)
                    .ConfigureAwait(false);
                var uomsById = await context.Set<Uom>().ToDictionaryAsync(uom => uom.Id, token).ConfigureAwait(false);
                var taxClassesById = await context.Set<TaxClass>().ToDictionaryAsync(taxClass => taxClass.Id, token)
                    .ConfigureAwait(false);

                var variantIds = singleVariantByProductId.Values.Select(variant => variant.Id).ToList();

                var balancesByVariantId = variantIds.Count == 0
                    ? []
                    : (await connection.QueryAsync<BalanceRow>(
                            new CommandDefinition(BalancesSql, new { VariantIds = variantIds }, cancellationToken: token))
                        .ConfigureAwait(false))
                        .ToDictionary(row => row.ProductVariantId);

                var primaryBarcodesByVariantId = await context.Set<Barcode>()
                    .Where(barcode => barcode.IsPrimary && variantIds.Contains(barcode.ProductVariantId))
                    .ToDictionaryAsync(barcode => barcode.ProductVariantId, token)
                    .ConfigureAwait(false);

                var rows = new List<CatalogueExportRow>(products.Count);

                foreach (var product in products)
                {
                    if (!singleVariantByProductId.TryGetValue(product.Id, out var variant))
                    {
                        continue;
                    }

                    var uom = uomsById[product.BaseUomId];
                    var taxClass = taxClassesById[product.TaxClassId];
                    var category = product.CategoryId is { } categoryId ? categoriesById.GetValueOrDefault(categoryId) : null;
                    var brand = product.BrandId is { } brandId ? brandsById.GetValueOrDefault(brandId) : null;
                    var balance = balancesByVariantId.GetValueOrDefault(variant.Id);
                    var barcode = primaryBarcodesByVariantId.GetValueOrDefault(variant.Id);

                    rows.Add(new CatalogueExportRow(
                        product.Code,
                        product.Name,
                        product.NameAlt,
                        category?.Name,
                        brand?.Name,
                        uom.Name,
                        Counterpoint.Domain.Catalogue.ProductTypes.Parse(product.Type),
                        taxClass.Name,
                        product.Location,
                        product.NonReturnable,
                        product.WarrantyDays,
                        product.Notes,
                        barcode?.Value,
                        variant.Price,
                        balance is null ? Money.Zero : Money.FromScaled(balance.CostAvg),
                        balance is null
                            ? Quantity.Zero(product.BaseUomId)
                            : Quantity.FromScaled(balance.QtyBase, product.BaseUomId)));
                }

                IReadOnlyList<CatalogueExportRow> result = rows;
                return result;
            },
            cancellationToken);

    /// <summary>One row of the balance projection, read raw rather than through the ledger's own EF entity.</summary>
    private sealed record BalanceRow(long ProductVariantId, long QtyBase, long CostAvg);
}
