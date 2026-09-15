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
/// Answers <see cref="IStockValuationQuery"/> off a read connection (task P2-T11 "Do this" #2).
/// Registered decorated with <see cref="Counterpoint.Application.Security.RoleAuthorisation"/>
/// inside this project's own <c>ReportingServiceCollectionExtensions</c> - this class is
/// <c>internal</c>, so only that extension can name it to build and wrap one (CLAUDE.md invariant
/// 8, the same discipline <c>SqliteAdjustmentHistoryQuery</c> and
/// <c>SqliteBulkBreakValueConservationQuery</c> already hold in <c>Counterpoint.Infrastructure</c>).
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over a read connection, not EF, reached through
/// <see cref="IReportConnectionFactory"/> because <c>Counterpoint.Reporting</c> may not reference
/// <c>Counterpoint.Infrastructure</c> (CLAUDE.md "Project boundaries").
/// </para>
/// <para>
/// <b>Scale, and why nothing is rounded here at all.</b> <c>qty_base</c> and <c>cost_avg</c> are
/// each stored scaled ×10 000 (<see cref="Quantity"/>, <see cref="Money"/>). Their raw SQL product
/// (<c>ValueRaw</c> below) is therefore scaled ×100 000 000 - eight implied decimal places.
/// <see cref="Money"/>'s own remarks are explicit that it is "not quantised on construction":
/// quantisation to four decimal places is something <see cref="Money.ToScaled"/> does on the way
/// to the database, and this report never writes one back. <see cref="TotalSql"/> sums the raw
/// column across the whole table with plain 64-bit integer arithmetic (no floating point), and
/// <see cref="Money.FromDecimal"/> then holds an exact <see cref="decimal"/> division of that sum
/// by 100 000 000 with no rounding step at all - decimal division by a power of ten loses nothing
/// within <see cref="decimal"/>'s own precision. That is what "ties to
/// <c>sum(stock_balance.qty_base × cost_avg)</c> exactly" (task P2-T11's own "Done when") means
/// here: exactly, not "exactly up to a rounding step".
/// </para>
/// <para>
/// Each line's own <see cref="StockValuationLine.Value"/> is computed the same exact way, so the
/// lines do sum to <see cref="StockValuationReport.TotalValue"/> to the last representable digit -
/// there is no independent rounding anywhere in this class for them to drift apart over.
/// </para>
/// <para>
/// No filter by <c>product.active</c> or <c>product_variant.active</c>: a valuation is "how much
/// capital is on the shelf right now", and a discontinued line still occupies it. Every row in
/// <c>stock_balance</c> - including a negative balance (Q-11) - is included.
/// </para>
/// </remarks>
internal sealed class StockValuationQuery : IStockValuationQuery
{
    /// <summary><see cref="Money.MoneyScale"/> × <see cref="Quantity.QtyScale"/> - the scale <c>ValueRaw</c> carries.</summary>
    private const decimal ValueRawScale = 100_000_000m;

    private const string LinesSql =
        """
        SELECT sb.product_variant_id AS ProductVariantId,
               p.name AS ProductDescription,
               pv.sku AS Sku,
               u.symbol AS BaseUomSymbol,
               p.base_uom_id AS BaseUomId,
               sb.qty_base AS QtyBaseScaled,
               sb.cost_avg AS CostAvgScaled,
               sb.qty_base * sb.cost_avg AS ValueRaw
          FROM stock_balance sb
          JOIN product_variant pv ON pv.id = sb.product_variant_id
          JOIN product p ON p.id = pv.product_id
          JOIN uom u ON u.id = p.base_uom_id
         ORDER BY ValueRaw DESC;
        """;

    private const string TotalSql =
        "SELECT COALESCE(SUM(qty_base * cost_avg), 0) FROM stock_balance;";

    private readonly IReportConnectionFactory _connectionFactory;

    public StockValuationQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<StockValuationReport> GetValuationAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var linesCommand = new CommandDefinition(LinesSql, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<Row>(linesCommand).ConfigureAwait(false);

            var totalCommand = new CommandDefinition(TotalSql, cancellationToken: cancellationToken);
            var totalRaw = await connection.ExecuteScalarAsync<long>(totalCommand).ConfigureAwait(false);

            IReadOnlyList<StockValuationLine> lines = [.. rows.Select(ToLine)];
            var totalValue = Money.FromDecimal(totalRaw / ValueRawScale);

            return new StockValuationReport(lines, totalValue);
        }
    }

    private static StockValuationLine ToLine(Row row) => new(
        row.ProductVariantId,
        row.ProductDescription,
        row.Sku,
        row.BaseUomSymbol,
        Quantity.FromScaled(row.QtyBaseScaled, row.BaseUomId),
        Money.FromScaled(row.CostAvgScaled),
        Money.FromDecimal(row.ValueRaw / ValueRawScale));

    /// <summary>The flat shape Dapper maps a row of <see cref="LinesSql"/> onto.</summary>
    private sealed class Row
    {
        public long ProductVariantId { get; set; }

        public string ProductDescription { get; set; } = string.Empty;

        public string Sku { get; set; } = string.Empty;

        public string BaseUomSymbol { get; set; } = string.Empty;

        public long BaseUomId { get; set; }

        public long QtyBaseScaled { get; set; }

        public long CostAvgScaled { get; set; }

        public long ValueRaw { get; set; }
    }
}
