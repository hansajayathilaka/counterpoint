using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Dashboard;

/// <summary>
/// Answers <see cref="IDashboardReader"/> off a read connection (SRS FR-9.7, NFR-P1 in spirit -
/// nothing here may compete with the single write connection a sale is using).
/// </summary>
/// <remarks>
/// <para>
/// <b>Today's sales</b> reads <c>sale</c> through its <c>business_date</c> index
/// (<c>ix_sale_date</c>, docs/01_DATA_MODEL.md §5) - the same column every rollup in this system
/// groups by - and excludes a cancelled bill, the same way <c>CompleteSaleHandler</c>'s own
/// reconciliation identity only ever counts a completed one.
/// </para>
/// <para>
/// <b>Cash in drawer</b> is the open shift's <c>opening_float</c> plus the cash actually taken
/// against it so far. Phase 1 has no cash-in/cash-out movements (<c>cash_movement</c> is
/// P3-T01's), so this is deliberately not the shift's true expected drawer total yet - it is
/// exactly what P1-T14 can honestly compute today. Scoped to one shift's payments, never a scan
/// of <c>payment</c> as a whole.
/// </para>
/// <para>
/// <b>Low stock count</b> sums <c>stock_balance.qty_base</c> across a product's variants and
/// compares it to <c>product.reorder_level</c> - both quantities scaled ×10 000 the same way
/// (CLAUDE.md invariant 1, docs/01_DATA_MODEL.md §2). A product whose reorder level is still the
/// default zero is never counted: zero means "not tracked", not "reorder immediately" (SRS
/// FR-2.2, FR-4.17).
/// </para>
/// </remarks>
internal sealed class SqliteDashboardReader : IDashboardReader
{
    private const string TodaysSalesSql =
        """
        SELECT COUNT(*) AS BillCount, COALESCE(SUM(total), 0) AS TotalScaled
          FROM sale
         WHERE business_date = @BusinessDate AND status = 'COMPLETED';
        """;

    private const string CashInDrawerSql =
        """
        SELECT s.opening_float AS OpeningFloatScaled,
               COALESCE((SELECT SUM(p.amount)
                           FROM payment p
                           JOIN sale sa ON sa.id = p.sale_id
                          WHERE sa.shift_id = s.id
                            AND sa.status = 'COMPLETED'
                            AND p.tender_type = 'CASH'), 0) AS CashSalesScaled
          FROM shift s
         WHERE s.status = 'OPEN'
         LIMIT 1;
        """;

    private const string LowStockCountSql =
        """
        SELECT COUNT(*) FROM (
          SELECT p.id
            FROM product p
            JOIN product_variant pv ON pv.product_id = p.id AND pv.active = 1
            LEFT JOIN stock_balance sb ON sb.product_variant_id = pv.id
           WHERE p.active = 1 AND p.reorder_level > 0
           GROUP BY p.id, p.reorder_level
          HAVING COALESCE(SUM(sb.qty_base), 0) <= p.reorder_level
        );
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteDashboardReader(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<DashboardSalesSummary> GetTodaysSalesAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(
                TodaysSalesSql,
                new { BusinessDate = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
                cancellationToken: cancellationToken);

            var row = await connection.QuerySingleAsync<SalesRow>(command).ConfigureAwait(false);

            return new DashboardSalesSummary(row.BillCount, Money.FromScaled(row.TotalScaled));
        }
    }

    /// <inheritdoc />
    public async Task<Money?> GetCashInDrawerAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(CashInDrawerSql, cancellationToken: cancellationToken);
            var row = await connection.QueryFirstOrDefaultAsync<CashRow>(command).ConfigureAwait(false);

            return row is null
                ? null
                : Money.FromScaled(row.OpeningFloatScaled) + Money.FromScaled(row.CashSalesScaled);
        }
    }

    /// <inheritdoc />
    public async Task<int> GetLowStockCountAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(LowStockCountSql, cancellationToken: cancellationToken);

            return await connection.ExecuteScalarAsync<int>(command).ConfigureAwait(false);
        }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="TodaysSalesSql"/> onto.</summary>
    private sealed class SalesRow
    {
        public int BillCount { get; set; }

        public long TotalScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="CashInDrawerSql"/> onto.</summary>
    private sealed class CashRow
    {
        public long OpeningFloatScaled { get; set; }

        public long CashSalesScaled { get; set; }
    }
}
