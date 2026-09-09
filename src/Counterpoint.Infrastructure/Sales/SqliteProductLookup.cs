using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Reads a sellable variant off a read connection (SRS NFR-P1, P1-T06 FR-2.9-2.12).
/// </summary>
/// <remarks>
/// <para>
/// A read connection, not the write one: a barcode scan must not queue behind the writer, and
/// the catalogue is not written by the sale transaction, so there is nothing uncommitted for it
/// to miss. WAL makes the read lock-free.
/// </para>
/// <para>
/// Dapper over one prepared statement, not EF: this is the hottest lookup in the system
/// (NFR-P1 - barcode scan to line on the bill in under 300 ms with 20 000 SKUs), its plan is the
/// unique index on <c>barcode.barcode</c>, and it must stay that way (docs/01_DATA_MODEL.md §12,
/// CLAUDE.md "Stack"). There is no EF change tracking anywhere on this path.
/// </para>
/// <para>
/// The stock balance projection is joined into the same statement, <c>LEFT JOIN</c> because a
/// variant that has never had a movement posted has no stock balance row at all - a
/// missing row and a zero balance (or a zero cost) mean the same thing, so <c>COALESCE</c> makes
/// them read the same way too. This is a read of the projection, never a sum of the ledger
/// (CLAUDE.md invariant 3 - the ledger stays the only place a balance is computed from).
/// </para>
/// <para>
/// <b>P1-T10:</b> <see cref="CatalogueItem.UnitCost"/> - the figure a sale snapshots onto
/// <c>sale_line.unit_cost</c> as its COGS - comes from <c>stock_balance.cost_avg</c>, not
/// <c>product.cost_avg</c>. The two are different columns: <c>stock_balance.cost_avg</c> is the
/// one the moving-average formula in <c>StockLedgerMath</c> actually maintains, on every posting
/// (P1-T07); <c>product.cost_avg</c> is set once, by the product editor, and nothing keeps it in
/// step with a receipt or a sale afterwards. Reading the wrong one would snapshot a COGS the
/// ledger has already moved past.
/// </para>
/// <para>
/// <b>P1-T09:</b> a second, small query on the same connection fetches the product's
/// <c>product_uom</c> rows - typically one to five per product - so the sale path can switch a
/// line's selling unit and reprice it (SRS FR-2.5, FR-3.7) without a second scan. This is a
/// bounded, indexed lookup by <c>product_id</c>, not a search, so it does not put NFR-P1's budget
/// at risk the way a second unbounded query would.
/// </para>
/// </remarks>
internal sealed class SqliteProductLookup : IProductLookup
{
    private const string SelectColumns =
        """
        SELECT pv.id AS ProductVariantId,
               p.id AS ProductId,
               p.code AS ProductCode,
               p.type AS ProductType,
               p.name AS Description,
               p.base_uom_id AS BaseUomId,
               u.symbol AS UomSymbol,
               pv.price AS UnitPrice,
               COALESCE(sb.cost_avg, 0) AS UnitCost,
               tc.rate AS TaxRate,
               COALESCE(sb.qty_base, 0) AS QtyBase,
               p.max_discount_rate AS MaxDiscountRate
          FROM product_variant pv
          JOIN product      p  ON p.id  = pv.product_id
          JOIN uom          u  ON u.id  = p.base_uom_id
          JOIN tax_class    tc ON tc.id = p.tax_class_id
          LEFT JOIN stock_balance sb ON sb.product_variant_id = pv.id
        """;

    private const string ByBarcodeSql =
        SelectColumns + """

          JOIN barcode b ON b.product_variant_id = pv.id
         WHERE b.barcode = @Barcode AND pv.active = 1 AND p.active = 1
         LIMIT 1;
        """;

    private const string ByVariantIdSql =
        SelectColumns + """

         WHERE pv.id = @Id AND pv.active = 1 AND p.active = 1
         LIMIT 1;
        """;

    private const string UomOptionsSql =
        """
        SELECT pu.uom_id AS UomId,
               ou.symbol AS Symbol,
               ou.decimal_places AS DecimalPlaces,
               pu.conversion_factor AS ConversionFactor,
               pu.is_base AS IsBase,
               pu.selling_price AS SellingPrice
          FROM product_uom pu
          JOIN uom ou ON ou.id = pu.uom_id
         WHERE pu.product_id = @ProductId
         ORDER BY pu.is_base DESC, ou.symbol;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteProductLookup(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public Task<CatalogueItem?> FindByBarcodeAsync(
        string barcode,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        return QueryAsync(ByBarcodeSql, new { Barcode = barcode }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogueItem?> FindByVariantIdAsync(
        long productVariantId,
        CancellationToken cancellationToken = default) =>
        QueryAsync(ByVariantIdSql, new { Id = productVariantId }, cancellationToken);

    private async Task<CatalogueItem?> QueryAsync(
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
            var row = await connection.QueryFirstOrDefaultAsync<Row>(command).ConfigureAwait(false);

            if (row is null)
            {
                return null;
            }

            var uomCommand = new CommandDefinition(
                UomOptionsSql,
                new { ProductId = row.ProductId },
                cancellationToken: cancellationToken);
            var uomRows = await connection.QueryAsync<UomOptionRow>(uomCommand).ConfigureAwait(false);

            return ToItem(row, uomRows);
        }
    }

    private static CatalogueItem ToItem(Row row, IEnumerable<UomOptionRow> uomRows) => new(
        row.ProductVariantId,
        row.ProductId,
        row.ProductCode,
        ProductTypes.Parse(row.ProductType),
        row.Description,
        row.BaseUomId,
        row.UomSymbol,
        Money.FromScaled(row.UnitPrice),
        Money.FromScaled(row.UnitCost),
        TaxRate.FromScaled(row.TaxRate),
        Quantity.FromScaled(row.QtyBase, row.BaseUomId),
        row.MaxDiscountRate is { } rate ? Percentage.FromScaled(rate) : null,
        [.. uomRows.Select(ToOption)]);

    private static ProductUomOption ToOption(UomOptionRow row) => new(
        row.UomId,
        row.Symbol,
        row.DecimalPlaces,
        UomConversion.FromScaled(row.ConversionFactor),
        row.IsBase != 0,
        row.SellingPrice is { } price ? Money.FromScaled(price) : null);

    /// <summary>The flat shape Dapper maps a row of <see cref="SelectColumns"/> onto.</summary>
    private sealed class Row
    {
        public long ProductVariantId { get; set; }

        public long ProductId { get; set; }

        public string ProductCode { get; set; } = string.Empty;

        public string ProductType { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long BaseUomId { get; set; }

        public string UomSymbol { get; set; } = string.Empty;

        public long UnitPrice { get; set; }

        public long UnitCost { get; set; }

        public long TaxRate { get; set; }

        public long QtyBase { get; set; }

        public long? MaxDiscountRate { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="UomOptionsSql"/> onto.</summary>
    private sealed class UomOptionRow
    {
        public long UomId { get; set; }

        public string Symbol { get; set; } = string.Empty;

        public int DecimalPlaces { get; set; }

        public long ConversionFactor { get; set; }

        public long IsBase { get; set; }

        public long? SellingPrice { get; set; }
    }
}
