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
/// <c>last_movement</c> join always finds a row: a positive balance cannot exist without at least
/// one posted movement (CLAUDE.md invariant 3).
/// </para>
/// </remarks>
internal sealed class SlowMovingStockQuery : ISlowMovingStockQuery
{
    /// <summary>
    /// Matches <c>Counterpoint.Infrastructure.Data.Iso8601TimestampConverter.Format</c> exactly -
    /// see this class's own remarks for why it is copied rather than referenced.
    /// </summary>
    private const string Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    private const string Sql =
        """
        SELECT sb.product_variant_id AS ProductVariantId,
               p.name AS ProductDescription,
               pv.sku AS Sku,
               u.symbol AS BaseUomSymbol,
               p.base_uom_id AS BaseUomId,
               sb.qty_base AS QtyOnHandScaled,
               lm.LastMovementAt AS LastMovementAtText
          FROM stock_balance sb
          JOIN product_variant pv ON pv.id = sb.product_variant_id AND pv.active = 1
          JOIN product p ON p.id = pv.product_id AND p.active = 1
          JOIN uom u ON u.id = p.base_uom_id
          JOIN (
                SELECT product_variant_id, MAX(occurred_at) AS LastMovementAt
                  FROM stock_movement
                 GROUP BY product_variant_id
               ) lm ON lm.product_variant_id = sb.product_variant_id
         WHERE sb.qty_base > 0
           AND lm.LastMovementAt <= @OlderThan
         ORDER BY lm.LastMovementAt ASC, p.code;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public SlowMovingStockQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SlowMovingStockLine>> FindAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(
                Sql,
                new { OlderThan = olderThan.ToString(Iso8601Format, CultureInfo.InvariantCulture) },
                cancellationToken: cancellationToken);

            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            IReadOnlyList<SlowMovingStockLine> result = [.. rows.Select(ToLine)];
            return result;
        }
    }

    private static SlowMovingStockLine ToLine(Row row) => new(
        row.ProductVariantId,
        row.ProductDescription,
        row.Sku,
        row.BaseUomSymbol,
        Quantity.FromScaled(row.QtyOnHandScaled, row.BaseUomId),
        DateTimeOffset.ParseExact(
            row.LastMovementAtText, Iso8601Format, CultureInfo.InvariantCulture, DateTimeStyles.None));

    /// <summary>The flat shape Dapper maps a row of <see cref="Sql"/> onto.</summary>
    private sealed class Row
    {
        public long ProductVariantId { get; set; }

        public string ProductDescription { get; set; } = string.Empty;

        public string Sku { get; set; } = string.Empty;

        public string BaseUomSymbol { get; set; } = string.Empty;

        public long BaseUomId { get; set; }

        public long QtyOnHandScaled { get; set; }

        public string LastMovementAtText { get; set; } = string.Empty;
    }
}
