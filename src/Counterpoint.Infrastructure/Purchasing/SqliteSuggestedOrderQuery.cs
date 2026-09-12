using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Purchasing;

/// <summary>
/// Answers <see cref="ISuggestedOrderQuery"/> off a read connection (SRS FR-4.6, task P2-T06 "Do
/// this" #3), the same split <c>SqliteDashboardReader.GetLowStockCountAsync</c> already draws for
/// exactly this comparison.
/// </summary>
/// <remarks>
/// Hand-written SQL over a read connection, not EF - this is a report query on the hot read path
/// (CLAUDE.md "Stack"), and it must never compete with the single write connection a sale or a
/// goods receipt is using. It sums the stock balance projection's quantity on hand across a
/// product's active variants and compares it to <c>product.reorder_level</c>, both scaled ×10 000
/// the same way (CLAUDE.md invariant 1) - it never sums the movement ledger itself (CLAUDE.md
/// invariant 3: a read path uses the projection, never the ledger).
/// </remarks>
internal sealed class SqliteSuggestedOrderQuery : ISuggestedOrderQuery
{
    private const string Sql =
        """
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
         ORDER BY (p.reorder_level - COALESCE(SUM(sb.qty_base), 0)) DESC, p.code;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteSuggestedOrderQuery(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SuggestedOrderLine>> GetSuggestedOrderAsync(
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(Sql, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            IReadOnlyList<SuggestedOrderLine> result = [.. rows.Select(ToLine)];
            return result;
        }
    }

    private static SuggestedOrderLine ToLine(Row row) => new(
        row.ProductId,
        row.ProductCode,
        row.ProductDescription,
        Quantity.FromScaled(row.QtyOnHandScaled, row.BaseUomId),
        Quantity.FromScaled(row.ReorderLevelScaled, row.BaseUomId),
        Quantity.FromScaled(row.SuggestedQtyScaled, row.BaseUomId),
        row.BaseUomSymbol);

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
    }
}
