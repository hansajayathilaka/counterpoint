using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Inventory;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Reporting.Queries;
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
/// each stored scaled ×10 000 (<see cref="Quantity"/>, <see cref="Money"/>). Since P3-T06 the
/// multiplication happens in C# <see cref="decimal"/> - a cost times a quantity is exact at eight decimal
/// places - rather than as a scaled-times-scaled product in SQL (which SQL must never do with money).
/// <see cref="Money"/>'s own remarks are explicit that it is "not quantised on construction":
/// quantisation to four decimal places is something <see cref="Money.ToScaled"/> does on the way
/// to the database, and this report never writes one back. The total is the exact sum of the lines,
/// so it ties to <c>sum(stock_balance.qty_base × cost_avg)</c> exactly (task P2-T11's own "Done
/// when"): exactly, not "exactly up to a rounding step". The value at selling price is the same
/// construction over <c>product_variant.price</c>.
/// </para>
/// <para>
/// Each line's own <see cref="StockValuationLine.Value"/> is computed the same exact way, so the
/// lines do sum to <see cref="StockValuationReport.TotalValue"/> to the last representable digit -
/// there is no independent rounding anywhere in this class for them to drift apart over.
/// <b>"As at" is now:</b> the valuation reads current balances and current costs; a past date
/// cannot be reconstructed from them and the ledger is never summed to fake one.
/// </para>
/// <para>
/// No filter by <c>product.active</c> or <c>product_variant.active</c>: a valuation is "how much
/// capital is on the shelf right now", and a discontinued line still occupies it. Every row in
/// <c>stock_balance</c> - including a negative balance (Q-11) - is included. A category filter
/// matches the category itself and its children (categories are two levels).
/// </para>
/// </remarks>
internal sealed class StockValuationQuery : IStockValuationQuery
{
    private const string LinesSql =
        """
        SELECT sb.product_variant_id AS ProductVariantId,
               p.name AS ProductDescription,
               pv.sku AS Sku,
               u.symbol AS BaseUomSymbol,
               p.base_uom_id AS BaseUomId,
               sb.qty_base AS QtyBaseScaled,
               sb.cost_avg AS CostAvgScaled,
               pv.price AS PriceScaled,
               COALESCE(c.name, '') AS CategoryName
          FROM stock_balance sb
          JOIN product_variant pv ON pv.id = sb.product_variant_id
          JOIN product p ON p.id = pv.product_id
          JOIN uom u ON u.id = p.base_uom_id
          LEFT JOIN category c ON c.id = p.category_id
         WHERE (@CategoryId IS NULL OR p.category_id = @CategoryId
                OR p.category_id IN (SELECT id FROM category WHERE parent_id = @CategoryId));
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public StockValuationQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public Task<StockValuationReport> GetValuationAsync(CancellationToken cancellationToken = default) =>
        GetValuationAsync(new StockValuationFilter(), cancellationToken);

    /// <inheritdoc />
    public async Task<StockValuationReport> GetValuationAsync(
        StockValuationFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(
                LinesSql, new { filter.CategoryId }, cancellationToken: cancellationToken);
            var rows = await connection.QueryAsync<Row>(command).ConfigureAwait(false);

            IReadOnlyList<StockValuationLine> lines =
            [
                .. rows.Select(ToLine)
                    .OrderByDescending(line => line.Value)
                    .ThenBy(line => line.Sku, StringComparer.Ordinal),
            ];

            return new StockValuationReport(
                lines,
                CanonicalFigures.Sum(lines.Select(line => line.Value)),
                CanonicalFigures.Sum(lines.Select(line => line.ValueAtSellingPrice)));
        }
    }

    // Cost and price times quantity, multiplied here in decimal - exact, never rounded, never a scaled
    // product in SQL.
    private static StockValuationLine ToLine(Row row)
    {
        var qty = Quantity.FromScaled(row.QtyBaseScaled, row.BaseUomId);
        var cost = Money.FromScaled(row.CostAvgScaled);
        var price = Money.FromScaled(row.PriceScaled);

        return new StockValuationLine(
            row.ProductVariantId,
            row.ProductDescription,
            row.Sku,
            row.BaseUomSymbol,
            qty,
            cost,
            cost * qty.Value,
            row.CategoryName,
            price,
            price * qty.Value);
    }

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

        public long PriceScaled { get; set; }

        public string CategoryName { get; set; } = string.Empty;
    }
}
