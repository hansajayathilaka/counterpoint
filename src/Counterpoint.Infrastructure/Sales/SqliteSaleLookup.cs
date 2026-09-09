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

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Reads a completed sale back for cancellation, off a read connection (SRS FR-3.34).
/// </summary>
/// <remarks>
/// Two queries on the one connection: the sale header, then every <c>stock_movement</c> row it
/// posted (<c>ref_doc_type = 'SALE'</c>, <c>ref_doc_id</c> = the sale), read through
/// <c>ix_movement_ref</c>. Both tables are append-only, so nothing read here can be invalidated
/// by anything that happens between this read and the cancellation transaction it feeds.
/// </remarks>
internal sealed class SqliteSaleLookup : ISaleLookup
{
    private const string SaleSql =
        """
        SELECT id AS Id, bill_no AS BillNo, status AS Status, business_date AS BusinessDate,
               sold_at AS SoldAt, total AS Total
          FROM sale
         WHERE id = @SaleId
         LIMIT 1;
        """;

    private const string MovementsSql =
        """
        SELECT product_variant_id AS ProductVariantId, qty_base AS QtyBase, unit_cost AS UnitCost
          FROM stock_movement
         WHERE ref_doc_type = 'SALE' AND ref_doc_id = @SaleId
         ORDER BY id;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteSaleLookup(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<SaleForCancellation?> FindForCancellationAsync(
        long saleId,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var saleCommand = new CommandDefinition(SaleSql, new { SaleId = saleId }, cancellationToken: cancellationToken);
            var sale = await connection.QueryFirstOrDefaultAsync<SaleRow>(saleCommand).ConfigureAwait(false);

            if (sale is null)
            {
                return null;
            }

            var movementsCommand = new CommandDefinition(
                MovementsSql,
                new { SaleId = saleId },
                cancellationToken: cancellationToken);
            var movements = await connection.QueryAsync<MovementRow>(movementsCommand).ConfigureAwait(false);

            return ToSaleForCancellation(sale, movements);
        }
    }

    private static SaleForCancellation ToSaleForCancellation(SaleRow sale, IEnumerable<MovementRow> movements) => new(
        sale.Id,
        sale.BillNo,
        sale.Status,
        DateOnly.ParseExact(sale.BusinessDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTimeOffset.Parse(sale.SoldAt, CultureInfo.InvariantCulture),
        Money.FromScaled(sale.Total),
        [.. movements.Select(ToReversal)]);

    /// <summary>
    /// The original outbound movement, flipped to what the reversal needs: a positive quantity
    /// (SRS FR-3.34, CLAUDE.md invariant 3) at the same cost it was sold at.
    /// </summary>
    private static SaleStockReversal ToReversal(MovementRow row) => new(
        row.ProductVariantId,

        // The tag is a bookkeeping convenience here, not a real uom id - the same convention
        // RebuildStockBalanceCommand uses (P1-T07): StockLedgerMath only needs a consistent tag
        // between the projection's qty and the movement's, and stock_movement carries no uom_id
        // column to read a real one from.
        Quantity.FromScaled(Math.Abs(row.QtyBase), row.ProductVariantId),
        Money.FromScaled(row.UnitCost));

    /// <summary>The flat shape Dapper maps a row of <see cref="SaleSql"/> onto.</summary>
    private sealed class SaleRow
    {
        public long Id { get; set; }

        public string BillNo { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public string SoldAt { get; set; } = string.Empty;

        public long Total { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="MovementsSql"/> onto.</summary>
    private sealed class MovementRow
    {
        public long ProductVariantId { get; set; }

        public long QtyBase { get; set; }

        public long UnitCost { get; set; }
    }
}
