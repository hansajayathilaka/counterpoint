using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Dashboard;

/// <summary>
/// Answers <see cref="IRecentSalesQuery"/> off a read connection (SRS FR-9.7, task P3-T22): the
/// last N completed sales, most recent first.
/// </summary>
/// <remarks>
/// <para>
/// Ordered by <c>sold_at</c> - the sale's own completion timestamp, the same column
/// <c>SqliteReturnableSaleLookup.SearchAsync</c> already orders "most recent first" by, and the
/// one this DTO's own <see cref="RecentSale.CompletedAt"/> reports - not <c>business_date</c>,
/// which is a calendar day (<c>YYYY-MM-DD</c>) and cannot break a tie between two bills completed
/// on the same day. <c>ix_sale_soldat</c> (docs/01_DATA_MODEL.md §5) already exists precisely for
/// this column, so <c>ORDER BY sold_at DESC LIMIT @Count</c> walks it in order and stops after
/// <c>@Count</c> matching rows - no new index, and no separate sort step, confirmed by
/// <c>EXPLAIN QUERY PLAN</c> against the 100,000-line seeded database
/// (<c>RecentSalesQueryTests</c>'s own performance test records the plan and the timing).
/// </para>
/// <para>
/// No role requirement of its own - see <see cref="IRecentSalesQuery"/>'s own remarks.
/// </para>
/// </remarks>
internal sealed class SqliteRecentSalesQuery : IRecentSalesQuery
{
    private const string RecentSalesSql =
        """
        SELECT s.bill_no AS BillNo, s.sold_at AS SoldAt,
               COALESCE(c.name, 'Walk-in') AS CustomerName, s.total AS Total
          FROM sale s
          LEFT JOIN customer c ON c.id = s.customer_id
         WHERE s.status = 'COMPLETED'
         ORDER BY s.sold_at DESC
         LIMIT @Count;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteRecentSalesQuery(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecentSale>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(count, 0);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(RecentSalesSql, new { Count = count }, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            return [.. rows.Select(ToRecentSale)];
        }
    }

    private static RecentSale ToRecentSale(Row row) => new(
        row.BillNo,
        DateTimeOffset.Parse(row.SoldAt, CultureInfo.InvariantCulture),
        row.CustomerName,
        Money.FromScaled(row.Total));

    /// <summary>The flat shape Dapper maps a row of <see cref="RecentSalesSql"/> onto.</summary>
    private sealed class Row
    {
        public string BillNo { get; set; } = string.Empty;

        public string SoldAt { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public long Total { get; set; }
    }
}
