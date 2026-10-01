using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Inventory;

/// <summary>
/// <see cref="IStockOnHandQuery"/>: the stock-on-hand list (task P3-T06 "Do this" #7, SRS RPT-08).
/// </summary>
/// <remarks>
/// <para>
/// <b>Cost-free by construction.</b> Not owner-only (SRS lists RPT-08 for both roles), so this query's SQL has
/// no cost column at all and its DTO has no cost field: there is no flag choosing between a costed and a
/// cost-free read, because there is only the cost-free one (CLAUDE.md invariant 8). It reads the
/// <c>stock_balance</c> projection, never the ledger (invariant 3).
/// </para>
/// <para>
/// Alternate units divide the base quantity by the unit's <c>product_uom.conversion_factor</c> (base units per
/// alternate unit) in C# decimal, unrounded.
/// </para>
/// </remarks>
internal sealed class StockOnHandQuery : IStockOnHandQuery
{
    private const string LinesSql =
        """
        SELECT pv.id AS ProductVariantId,
               p.id AS ProductId,
               pv.sku AS Sku,
               p.name AS Description,
               COALESCE(c.name, '') AS CategoryName,
               COALESCE(b.name, '') AS BrandName,
               COALESCE(p.location, '') AS Location,
               COALESCE(sb.qty_base, 0) AS QtyBaseScaled,
               p.base_uom_id AS BaseUomId,
               u.symbol AS BaseUomSymbol,
               p.reorder_level AS ReorderLevelScaled
          FROM product_variant pv
          JOIN product p ON p.id = pv.product_id
          JOIN uom u ON u.id = p.base_uom_id
          LEFT JOIN category c ON c.id = p.category_id
          LEFT JOIN brand b ON b.id = p.brand_id
          LEFT JOIN stock_balance sb ON sb.product_variant_id = pv.id
         WHERE pv.active = 1 AND p.active = 1
           AND p.type IN ('STANDARD', 'DECIMAL')
           AND (@CategoryId IS NULL OR p.category_id = @CategoryId
                OR p.category_id IN (SELECT id FROM category WHERE parent_id = @CategoryId))
           AND (@BrandId IS NULL OR p.brand_id = @BrandId)
           AND (@SupplierId IS NULL OR EXISTS (
                SELECT 1 FROM product_supplier ps WHERE ps.product_id = p.id AND ps.supplier_id = @SupplierId))
           AND (@Location IS NULL OR INSTR(LOWER(COALESCE(p.location, '')), LOWER(@Location)) > 0)
           AND (@InStockOnly = 0 OR COALESCE(sb.qty_base, 0) > 0)
         ORDER BY p.name, pv.sku;
        """;

    private const string AlternateUnitsSql =
        """
        SELECT pu.product_id AS ProductId, u.symbol AS Symbol, pu.conversion_factor AS FactorScaled
          FROM product_uom pu
          JOIN uom u ON u.id = pu.uom_id
         WHERE pu.is_base = 0 AND pu.conversion_factor > 0
         ORDER BY pu.product_id, pu.conversion_factor;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public StockOnHandQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<StockOnHandReport> GetStockOnHandAsync(
        StockOnHandFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var parameters = new
        {
            filter.CategoryId,
            filter.BrandId,
            filter.SupplierId,
            Location = string.IsNullOrWhiteSpace(filter.Location) ? null : filter.Location.Trim(),
            InStockOnly = filter.InStockOnly ? 1 : 0,
        };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = (await connection.QueryAsync<Row>(
                new CommandDefinition(LinesSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();

            var alternates = (await connection.QueryAsync<AlternateRow>(
                new CommandDefinition(AlternateUnitsSql, cancellationToken: cancellationToken)).ConfigureAwait(false))
                .ToLookup(row => row.ProductId);

            return new StockOnHandReport(
            [
                .. rows.Select(row => new StockOnHandLine(
                    row.ProductVariantId,
                    row.Sku,
                    row.Description,
                    row.CategoryName,
                    row.BrandName,
                    row.Location,
                    Quantity.FromScaled(row.QtyBaseScaled, row.BaseUomId),
                    row.BaseUomSymbol,
                    [
                        .. alternates[row.ProductId].Select(alternate => new StockOnHandAlternateUnit(
                            alternate.Symbol,
                            Quantity.FromScaled(row.QtyBaseScaled, row.BaseUomId).Value
                                / Quantity.FromScaled(alternate.FactorScaled, 0).Value)),
                    ],
                    Quantity.FromScaled(row.ReorderLevelScaled, row.BaseUomId))),
            ]);
        }
    }

    private sealed class Row
    {
        public long ProductVariantId { get; set; }

        public long ProductId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public string CategoryName { get; set; } = string.Empty;

        public string BrandName { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;

        public long QtyBaseScaled { get; set; }

        public long BaseUomId { get; set; }

        public string BaseUomSymbol { get; set; } = string.Empty;

        public long ReorderLevelScaled { get; set; }
    }

    private sealed class AlternateRow
    {
        public long ProductId { get; set; }

        public string Symbol { get; set; } = string.Empty;

        public long FactorScaled { get; set; }
    }
}
