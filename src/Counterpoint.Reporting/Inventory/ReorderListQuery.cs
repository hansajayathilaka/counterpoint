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
/// Answers <see cref="IReorderListQuery"/> off a read connection (task P2-T11 "Do this" #1).
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over a read connection, not EF - a report query on the hot read path
/// (CLAUDE.md "Stack"), the same split every <c>Sqlite*Query</c> reader in
/// <c>Counterpoint.Infrastructure</c> draws, reached here through
/// <see cref="IReportConnectionFactory"/> because <c>Counterpoint.Reporting</c> may not reference
/// <c>Counterpoint.Infrastructure</c> (CLAUDE.md "Project boundaries").
/// </para>
/// <para>
/// <b>Low-stock predicate.</b> Sums <c>stock_balance.qty_base</c> across a product's active
/// variants and compares it to <c>product.reorder_level</c> - the identical predicate
/// <c>SqliteDashboardReader.GetLowStockCountAsync</c> (the dashboard's low-stock count) and
/// <c>SqliteSuggestedOrderQuery</c> (task P2-T06's draft-order helper) already use, so all three
/// counts never drift apart. A product whose reorder level is still the default zero is never
/// proposed - zero means "not tracked".
/// </para>
/// <para>
/// <b>Preferred supplier, exactly as task P2-T11 specifies it (a data-modeler's own resolved
/// design, not re-derived here).</b> Zero linked <c>product_supplier</c> rows: no preferred
/// supplier - the product still appears in the list. Exactly one linked row: that supplier. Two
/// or more: the one with the most recent <c>goods_receipt.received_at</c> for that
/// product+supplier pair, joined through <c>goods_receipt_line -&gt; product_variant -&gt;
/// product_id</c> to <c>goods_receipt.supplier_id</c> (<c>grn_recency</c> below).
/// </para>
/// <para>
/// <b>The tie-break, spelled out because it reads like a business rule and is not one.</b> Two or
/// more linked suppliers that tie on the most-recent-receipt date, or that have no goods-receipt
/// history against this product at all, resolve to the lowest <c>supplier_id</c> - purely for a
/// deterministic result. It carries no purchasing meaning: a lower id is not a better supplier.
/// <c>ranked_multi</c>'s own <c>ORDER BY</c> encodes this in one expression: a null most-recent
/// date sorts after a real one (the <c>CASE</c> term), a real date sorts most-recent-first, and
/// <c>supplier_id ASC</c> breaks every remaining tie, including "every candidate is null".
/// </para>
/// <para>
/// No new index: this is not a hot sale-path query, and the seeded dataset is small enough for
/// the report's own 10-second budget (task P2-T11 "Done when") without one.
/// </para>
/// </remarks>
internal sealed class ReorderListQuery : IReorderListQuery
{
    private const string Sql =
        """
        WITH low_stock AS (
            SELECT p.id AS ProductId,
                   p.code AS ProductCode,
                   p.name AS ProductDescription,
                   COALESCE(SUM(sb.qty_base), 0) AS QtyOnHandScaled,
                   p.reorder_level AS ReorderLevelScaled,
                   p.reorder_qty AS SuggestedQtyScaled,
                   p.base_uom_id AS BaseUomId,
                   u.symbol AS BaseUomSymbol
              FROM product p
              JOIN product_variant pv ON pv.product_id = p.id AND pv.active = 1
              JOIN uom u ON u.id = p.base_uom_id
              LEFT JOIN stock_balance sb ON sb.product_variant_id = pv.id
             WHERE p.active = 1 AND p.reorder_level > 0
             GROUP BY p.id, p.code, p.name, p.reorder_level, p.reorder_qty, p.base_uom_id, u.symbol
            HAVING COALESCE(SUM(sb.qty_base), 0) <= p.reorder_level
        ),
        supplier_count AS (
            SELECT product_id, COUNT(*) AS LinkCount
              FROM product_supplier
             GROUP BY product_id
        ),
        single_supplier AS (
            SELECT ps.product_id, ps.supplier_id
              FROM product_supplier ps
              JOIN supplier_count sc ON sc.product_id = ps.product_id AND sc.LinkCount = 1
        ),
        grn_recency AS (
            SELECT ps.product_id, ps.supplier_id, MAX(gr.received_at) AS LastReceivedAt
              FROM product_supplier ps
              JOIN goods_receipt gr ON gr.supplier_id = ps.supplier_id
              JOIN goods_receipt_line grl ON grl.goods_receipt_id = gr.id
              JOIN product_variant pv2 ON pv2.id = grl.product_variant_id AND pv2.product_id = ps.product_id
             GROUP BY ps.product_id, ps.supplier_id
        ),
        ranked_multi AS (
            SELECT ps.product_id,
                   ps.supplier_id,
                   ROW_NUMBER() OVER (
                       PARTITION BY ps.product_id
                       ORDER BY CASE WHEN gr.LastReceivedAt IS NULL THEN 1 ELSE 0 END,
                                gr.LastReceivedAt DESC,
                                ps.supplier_id ASC
                   ) AS Rn
              FROM product_supplier ps
              JOIN supplier_count sc ON sc.product_id = ps.product_id AND sc.LinkCount >= 2
              LEFT JOIN grn_recency gr ON gr.product_id = ps.product_id AND gr.supplier_id = ps.supplier_id
        ),
        multi_supplier AS (
            SELECT product_id, supplier_id FROM ranked_multi WHERE Rn = 1
        ),
        preferred AS (
            SELECT product_id, supplier_id FROM single_supplier
            UNION ALL
            SELECT product_id, supplier_id FROM multi_supplier
        )
        SELECT ls.ProductId,
               ls.ProductCode,
               ls.ProductDescription,
               ls.QtyOnHandScaled,
               ls.ReorderLevelScaled,
               ls.SuggestedQtyScaled,
               ls.BaseUomId,
               ls.BaseUomSymbol,
               pr.supplier_id AS PreferredSupplierId,
               s.name AS PreferredSupplierName
          FROM low_stock ls
          LEFT JOIN preferred pr ON pr.product_id = ls.ProductId
          LEFT JOIN supplier s ON s.id = pr.supplier_id
         ORDER BY (ls.ReorderLevelScaled - ls.QtyOnHandScaled) DESC, ls.ProductCode;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public ReorderListQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReorderListLine>> GetReorderListAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            IReadOnlyList<ReorderListLine> result = [.. rows.Select(ToLine)];
            return result;
        }
    }

    private static ReorderListLine ToLine(Row row) => new(
        row.ProductId,
        row.ProductCode,
        row.ProductDescription,
        Quantity.FromScaled(row.QtyOnHandScaled, row.BaseUomId),
        Quantity.FromScaled(row.ReorderLevelScaled, row.BaseUomId),
        Quantity.FromScaled(row.SuggestedQtyScaled, row.BaseUomId),
        row.BaseUomSymbol,
        row.PreferredSupplierId,
        row.PreferredSupplierName);

    /// <summary>The flat shape Dapper maps a row of <see cref="Sql"/> onto.</summary>
    private sealed class Row
    {
        public long ProductId { get; set; }

        public string ProductCode { get; set; } = string.Empty;

        public string ProductDescription { get; set; } = string.Empty;

        public long QtyOnHandScaled { get; set; }

        public long ReorderLevelScaled { get; set; }

        public long SuggestedQtyScaled { get; set; }

        public long BaseUomId { get; set; }

        public string BaseUomSymbol { get; set; } = string.Empty;

        public long? PreferredSupplierId { get; set; }

        public string? PreferredSupplierName { get; set; }
    }
}
