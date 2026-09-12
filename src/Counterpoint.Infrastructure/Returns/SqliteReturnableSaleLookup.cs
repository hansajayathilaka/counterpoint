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

namespace Counterpoint.Infrastructure.Returns;

/// <summary>
/// Finds a completed bill and its lines for a linked return (SRS FR-5.1, task P2-T02 step 1), off
/// a read connection.
/// </summary>
/// <remarks>
/// The same two-query shape <c>SqliteSaleLookup</c> uses for cancellation: the bill header, then
/// every line, joined once to <c>product_variant</c>, <c>product</c> and <c>uom</c> so
/// <see cref="ReturnableSaleLine.NonReturnable"/> and <see cref="ReturnableSaleLine.CategoryId"/>
/// - what AC-05's non-returnable check needs - come back in the same round trip a screen would
/// need anyway, rather than a second lookup per line.
/// </remarks>
internal sealed class SqliteReturnableSaleLookup : IReturnableSaleLookup
{
    private const string SaleByIdSql =
        """
        SELECT id AS Id, bill_no AS BillNo, sold_at AS SoldAt, business_date AS BusinessDate,
               status AS Status, customer_id AS CustomerId, total AS Total
          FROM sale
         WHERE id = @SaleId
         LIMIT 1;
        """;

    private const string SaleByBillNoSql =
        """
        SELECT id AS Id, bill_no AS BillNo, sold_at AS SoldAt, business_date AS BusinessDate,
               status AS Status, customer_id AS CustomerId, total AS Total
          FROM sale
         WHERE bill_no = @BillNo
         LIMIT 1;
        """;

    private const string LinesSql =
        """
        SELECT sl.id AS SaleLineId, sl.product_variant_id AS ProductVariantId,
               sl.description AS Description, u.symbol AS UomSymbol,
               sl.qty_base AS QtySoldBase, sl.qty_returned AS QtyReturnedBase,
               sl.unit_price AS UnitPrice, sl.unit_cost AS UnitCost,
               sl.line_total AS LineTotal, sl.tax AS Tax,
               COALESCE(p.non_returnable, 0) AS NonReturnable, p.category_id AS CategoryId
          FROM sale_line sl
          LEFT JOIN uom u ON u.id = sl.uom_id
          LEFT JOIN product_variant pv ON pv.id = sl.product_variant_id
          LEFT JOIN product p ON p.id = pv.product_id
         WHERE sl.sale_id = @SaleId
         ORDER BY sl.line_no;
        """;

    private const string SearchSql =
        """
        SELECT s.id AS SaleId, s.bill_no AS BillNo, s.sold_at AS SoldAt,
               COALESCE(c.name, 'Walk-in') AS CustomerName, s.total AS Total
          FROM sale s
          LEFT JOIN customer c ON c.id = s.customer_id
         WHERE s.status = 'COMPLETED'
           AND (@FromDate IS NULL OR s.business_date >= @FromDate)
           AND (@ToDate IS NULL OR s.business_date <= @ToDate)
           AND (@CustomerName IS NULL OR c.name LIKE '%' || @CustomerName || '%')
           AND (@Amount IS NULL OR ABS(s.total - @Amount) <= @Tolerance)
         ORDER BY s.sold_at DESC
         LIMIT 50;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteReturnableSaleLookup(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<ReturnableSale?> FindByBillNoAsync(string billNo, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(billNo);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(SaleByBillNoSql, new { BillNo = billNo }, cancellationToken: cancellationToken);
            var sale = await connection.QueryFirstOrDefaultAsync<SaleRow>(command).ConfigureAwait(false);

            return sale is null ? null : await LoadAsync(connection, sale, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<ReturnableSale?> FindBySaleIdAsync(long saleId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(SaleByIdSql, new { SaleId = saleId }, cancellationToken: cancellationToken);
            var sale = await connection.QueryFirstOrDefaultAsync<SaleRow>(command).ConfigureAwait(false);

            return sale is null ? null : await LoadAsync(connection, sale, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReturnSaleSearchResult>> SearchAsync(
        ReturnSaleSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = new CommandDefinition(
                SearchSql,
                new
                {
                    FromDate = criteria.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ToDate = criteria.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    criteria.CustomerName,
                    Amount = criteria.Amount?.ToScaled(),
                    Tolerance = criteria.AmountTolerance?.ToScaled() ?? 0L,
                },
                cancellationToken: cancellationToken);

            var rows = await connection.QueryAsync<SearchRow>(command).ConfigureAwait(false);

            return [.. rows.Select(ToSearchResult)];
        }
    }

    private static async Task<ReturnableSale> LoadAsync(
        System.Data.Common.DbConnection connection, SaleRow sale, CancellationToken cancellationToken)
    {
        var linesCommand = new CommandDefinition(LinesSql, new { SaleId = sale.Id }, cancellationToken: cancellationToken);
        var lines = await connection.QueryAsync<LineRow>(linesCommand).ConfigureAwait(false);

        return ToReturnableSale(sale, lines);
    }

    private static ReturnableSale ToReturnableSale(SaleRow sale, IEnumerable<LineRow> lines) => new(
        sale.Id,
        sale.BillNo,
        DateTimeOffset.Parse(sale.SoldAt, CultureInfo.InvariantCulture),
        DateOnly.ParseExact(sale.BusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        sale.Status,
        sale.CustomerId,
        Money.FromScaled(sale.Total),
        [.. lines.Select(ToReturnableSaleLine)]);

    private static ReturnableSaleLine ToReturnableSaleLine(LineRow line)
    {
        // The same bookkeeping tag SqliteSaleLookup uses for a reversal quantity: StockLedgerMath
        // and this line's own arithmetic only need the two quantities on one line to agree with
        // each other, not a real uom id - sale_return_line carries no uom_id column to convert
        // back from either. An open item (no ProductVariantId) tags with its own line id instead,
        // so the two quantities still agree with each other even though there is no variant.
        var tag = line.ProductVariantId ?? line.SaleLineId;

        return new ReturnableSaleLine(
            line.SaleLineId,
            line.ProductVariantId,
            line.Description,
            line.UomSymbol ?? string.Empty,
            Quantity.FromScaled(line.QtySoldBase, tag),
            Quantity.FromScaled(line.QtyReturnedBase, tag),
            Money.FromScaled(line.UnitPrice),
            Money.FromScaled(line.UnitCost),
            Money.FromScaled(line.LineTotal),
            Money.FromScaled(line.Tax),
            line.NonReturnable != 0,
            line.CategoryId);
    }

    private static ReturnSaleSearchResult ToSearchResult(SearchRow row) => new(
        row.SaleId,
        row.BillNo,
        DateTimeOffset.Parse(row.SoldAt, CultureInfo.InvariantCulture),
        row.CustomerName,
        Money.FromScaled(row.Total));

    /// <summary>The flat shape Dapper maps a row of <see cref="SaleByIdSql"/>/<see cref="SaleByBillNoSql"/> onto.</summary>
    private sealed class SaleRow
    {
        public long Id { get; set; }

        public string BillNo { get; set; } = string.Empty;

        public string SoldAt { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public long? CustomerId { get; set; }

        public long Total { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="LinesSql"/> onto.</summary>
    private sealed class LineRow
    {
        public long SaleLineId { get; set; }

        public long? ProductVariantId { get; set; }

        public string Description { get; set; } = string.Empty;

        public string? UomSymbol { get; set; }

        public long QtySoldBase { get; set; }

        public long QtyReturnedBase { get; set; }

        public long UnitPrice { get; set; }

        public long UnitCost { get; set; }

        public long LineTotal { get; set; }

        public long Tax { get; set; }

        public long NonReturnable { get; set; }

        public long? CategoryId { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="SearchSql"/> onto.</summary>
    private sealed class SearchRow
    {
        public long SaleId { get; set; }

        public string BillNo { get; set; } = string.Empty;

        public string SoldAt { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public long Total { get; set; }
    }
}
