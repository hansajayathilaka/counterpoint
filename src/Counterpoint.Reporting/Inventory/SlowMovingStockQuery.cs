using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Inventory;

/// <summary>
/// Answers <see cref="ISlowMovingStockQuery"/> off a read connection (task P2-T11 "Do this" #3).
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over a read connection, not EF, reached through
/// <see cref="IReportConnectionFactory"/> because <c>Counterpoint.Reporting</c> may not reference
/// <c>Counterpoint.Infrastructure</c> (CLAUDE.md "Project boundaries").
/// </para>
/// <para>
/// <c>occurred_at</c> is stored as fixed-width ISO-8601 text
/// (<c>Counterpoint.Infrastructure.Data.Iso8601TimestampConverter</c>, format
/// <c>yyyy-MM-ddTHH:mm:ss.fffzzz</c>), so a plain string comparison orders it correctly - the same
/// reasoning every other date-range report in this codebase relies on. <see cref="Iso8601Format"/>
/// duplicates that exact format string rather than referencing the converter, because this project
/// cannot reference <c>Counterpoint.Infrastructure</c> at all (see the remarks above); the two must
/// be kept in lock-step by hand if either ever changes.
/// </para>
/// <para>
/// Requires <c>stock_balance.qty_base &gt; 0</c>, which is also what guarantees the inner
/// <c>movement_span</c> join always finds a row: a positive balance cannot exist without at least
/// one posted movement (CLAUDE.md invariant 3).
/// </para>
/// <para>
/// <b>"No sale in N days" (task P3-T06, decided).</b> A variant's idle time starts at its most recent
/// <c>SALE</c> movement - ignoring a bill that was later cancelled - or, when it has never sold, at its first
/// ledger movement (the day the stock first arrived). Receipts, returns, counts and adjustments never reset
/// it. Owner-only: the value tied up is <c>qty x cost_avg</c>, multiplied here in C# and never in SQL.
/// </para>
/// </remarks>
internal sealed class SlowMovingStockQuery : ISlowMovingStockQuery
{
    /// <summary>
    /// Matches <c>Counterpoint.Infrastructure.Data.Iso8601TimestampConverter.Format</c> exactly -
    /// see this class's own remarks for why it is copied rather than referenced.
    /// </summary>
    private const string Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    // A SALE movement whose bill was later cancelled is not a sale; a SALE movement with no resolvable
    // bill (ref_doc_id NULL) still counts. "Idle since" is the last counted sale, else the first movement.
    private const string Sql =
        """
        SELECT sb.product_variant_id AS ProductVariantId,
               p.name AS ProductDescription,
               pv.sku AS Sku,
               u.symbol AS BaseUomSymbol,
               p.base_uom_id AS BaseUomId,
               sb.qty_base AS QtyOnHandScaled,
               sb.cost_avg AS CostAvgScaled,
               COALESCE(c.name, '') AS CategoryName,
               ms.LastMovementAt AS LastMovementAtText,
               ms.FirstMovementAt AS FirstMovementAtText,
               ls.LastSaleAt AS LastSaleAtText
          FROM stock_balance sb
          JOIN product_variant pv ON pv.id = sb.product_variant_id AND pv.active = 1
          JOIN product p ON p.id = pv.product_id AND p.active = 1
          JOIN uom u ON u.id = p.base_uom_id
          LEFT JOIN category c ON c.id = p.category_id
          JOIN (
                SELECT product_variant_id,
                       MAX(occurred_at) AS LastMovementAt,
                       MIN(occurred_at) AS FirstMovementAt
                  FROM stock_movement
                 GROUP BY product_variant_id
               ) ms ON ms.product_variant_id = sb.product_variant_id
          LEFT JOIN (
                SELECT sm.product_variant_id, MAX(sm.occurred_at) AS LastSaleAt
                  FROM stock_movement sm
                  LEFT JOIN sale sa ON sm.ref_doc_type = 'SALE' AND sa.id = sm.ref_doc_id
                 WHERE sm.movement_type = 'SALE'
                   AND (sa.id IS NULL OR sa.status = 'COMPLETED')
                 GROUP BY sm.product_variant_id
               ) ls ON ls.product_variant_id = sb.product_variant_id
         WHERE sb.qty_base > 0
           AND COALESCE(ls.LastSaleAt, ms.FirstMovementAt) <= @OlderThan
           AND (@CategoryId IS NULL OR p.category_id = @CategoryId
                OR p.category_id IN (SELECT id FROM category WHERE parent_id = @CategoryId))
         ORDER BY COALESCE(ls.LastSaleAt, ms.FirstMovementAt) ASC, p.code;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public SlowMovingStockQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SlowMovingStockLine>> FindAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default) =>
        FindAsync(new SlowMovingFilter(olderThan), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlowMovingStockLine>> FindAsync(
        SlowMovingFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(
                Sql,
                new
                {
                    OlderThan = filter.OlderThan.ToString(Iso8601Format, CultureInfo.InvariantCulture),
                    filter.CategoryId,
                },
                cancellationToken: cancellationToken);

            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            IReadOnlyList<SlowMovingStockLine> result = [.. rows.Select(ToLine)];
            return result;
        }
    }

    private static SlowMovingStockLine ToLine(Row row)
    {
        var qty = Quantity.FromScaled(row.QtyOnHandScaled, row.BaseUomId);
        var cost = Money.FromScaled(row.CostAvgScaled);
        var lastSale = row.LastSaleAtText is null ? (DateTimeOffset?)null : Parse(row.LastSaleAtText);

        return new SlowMovingStockLine(
            row.ProductVariantId,
            row.ProductDescription,
            row.Sku,
            row.BaseUomSymbol,
            qty,
            Parse(row.LastMovementAtText),
            lastSale,
            lastSale ?? Parse(row.FirstMovementAtText),
            row.CategoryName,
            cost,
            cost * qty.Value);
    }

    private static DateTimeOffset Parse(string text) =>
        DateTimeOffset.ParseExact(text, Iso8601Format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>The flat shape Dapper maps a row of <see cref="Sql"/> onto.</summary>
    private sealed class Row
    {
        public long ProductVariantId { get; set; }

        public string ProductDescription { get; set; } = string.Empty;

        public string Sku { get; set; } = string.Empty;

        public string BaseUomSymbol { get; set; } = string.Empty;

        public long BaseUomId { get; set; }

        public long QtyOnHandScaled { get; set; }

        public long CostAvgScaled { get; set; }

        public string CategoryName { get; set; } = string.Empty;

        public string LastMovementAtText { get; set; } = string.Empty;

        public string FirstMovementAtText { get; set; } = string.Empty;

        public string? LastSaleAtText { get; set; }
    }
}
