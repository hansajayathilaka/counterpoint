using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Inventory;

/// <summary>
/// <see cref="IStockCardQuery"/>: the stock movement ledger / item stock card (task P3-T06 "Do this" #4,
/// SRS RPT-11).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b>, registered only wrapped with <c>RoleAuthorisation</c>.
/// </para>
/// <para>
/// <b>This is the one place a report reads the ledger row by row, and nothing here is summed from it into
/// a balance anything else uses</b> (CLAUDE.md invariant 3). The range's movements are found through
/// <c>ix_movement_variant_time</c> and listed in the ledger's own order, its <c>id</c>: the ledger is
/// append-only and every movement carries the <c>balance_after</c> of the one posted before it, so posting
/// order <i>is</i> the balance chain (usually chronological too, because a movement is stamped as it posts;
/// a back-dated receipt or adjustment is not, and is flagged). The opening balance is the <c>balance_after</c>
/// of the movement immediately before the first row in that order (zero when there is none) - read, not
/// summed.
/// </para>
/// <para>
/// <b>The chain is checked per row against the row's own predecessor in the ledger</b>, not against the row
/// above it on the card: each row's <c>balance_after - qty_base</c> must equal the <c>balance_after</c> of the
/// immediately preceding ledger movement for that variant (<c>LAG</c> over the variant's whole ledger - rows
/// outside the date range still count as the predecessor). A healthy ledger therefore reconciles whatever
/// subset of it a date range selects; only a row whose recorded balance does not follow from the one before it
/// fails. A row whose predecessor is dated later (posted out of date order) or is not the row shown above it
/// is flagged so the owner can see why the opening differs from the previous day's close.
/// </para>
/// <para>
/// A day is the wall-clock date at the front of <c>occurred_at</c> ("yyyy-MM-dd..."): a bare date string
/// sorts before every stamp on that day, so <c>occurred_at &gt;= From</c> and <c>occurred_at &lt; To + 1 day</c>
/// bound the range without depending on the UTC offset a movement was stamped with.
/// </para>
/// </remarks>
internal sealed class StockCardQuery : IStockCardQuery
{
    private const string Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    private const string VariantByIdSql =
        """
        SELECT pv.id AS ProductVariantId, pv.sku AS Sku, p.name AS Description,
               p.base_uom_id AS BaseUomId, u.symbol AS BaseUomSymbol
          FROM product_variant pv
          JOIN product p ON p.id = pv.product_id
          JOIN uom u ON u.id = p.base_uom_id
         WHERE pv.id = @ProductVariantId;
        """;

    private const string VariantBySkuSql =
        """
        SELECT pv.id AS ProductVariantId, pv.sku AS Sku, p.name AS Description,
               p.base_uom_id AS BaseUomId, u.symbol AS BaseUomSymbol
          FROM product_variant pv
          JOIN product p ON p.id = pv.product_id
          JOIN uom u ON u.id = p.base_uom_id
         WHERE pv.sku = @Key
            OR pv.id = (SELECT product_variant_id FROM barcode WHERE barcode = @Key)
         LIMIT 1;
        """;

    // With no movement in the range, the opening is the last movement dated before it (in the ledger's order).
    private const string OpeningBeforeDateSql =
        """
        SELECT balance_after
          FROM stock_movement
         WHERE product_variant_id = @ProductVariantId
           AND occurred_at < @From
         ORDER BY id DESC
         LIMIT 1;
        """;

    // The variant's whole ledger is windowed in id order so every row knows its predecessor, then the date
    // range is applied: a row's predecessor is whatever was posted before it, in range or not.
    private const string MovementsSql =
        """
        WITH ledger AS (
            SELECT id, occurred_at, movement_type, qty_base, unit_cost, ref_doc_type, ref_doc_id, note, balance_after,
                   LAG(id) OVER w AS prev_id,
                   LAG(balance_after) OVER w AS prev_balance_after,
                   LAG(occurred_at) OVER w AS prev_occurred_at
              FROM stock_movement
             WHERE product_variant_id = @ProductVariantId
            WINDOW w AS (ORDER BY id)
        )
        SELECT sm.id AS MovementId,
               sm.occurred_at AS OccurredAtText,
               sm.movement_type AS MovementType,
               sm.qty_base AS QtyBaseScaled,
               sm.unit_cost AS UnitCostScaled,
               sm.ref_doc_type AS RefDocType,
               sm.ref_doc_id AS RefDocId,
               COALESCE(sa.bill_no, gr.grn_no, rt.return_no, st.stock_take_no, '') AS ReferenceNo,
               sm.note AS Note,
               sm.balance_after AS BalanceAfterScaled,
               sm.prev_id AS PreviousMovementId,
               sm.prev_balance_after AS PreviousBalanceAfterScaled,
               sm.prev_occurred_at AS PreviousOccurredAtText
          FROM ledger sm
          LEFT JOIN sale sa ON sm.ref_doc_type = 'SALE' AND sa.id = sm.ref_doc_id
          LEFT JOIN goods_receipt gr ON sm.ref_doc_type = 'GRN' AND gr.id = sm.ref_doc_id
          LEFT JOIN sale_return rt ON sm.ref_doc_type = 'RETURN' AND rt.id = sm.ref_doc_id
          LEFT JOIN stock_take st ON sm.ref_doc_type = 'STOCK_TAKE' AND st.id = sm.ref_doc_id
         WHERE sm.occurred_at >= @From AND sm.occurred_at < @ToExclusive
         ORDER BY sm.id;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public StockCardQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public Task<StockCard?> GetStockCardAsync(
        long productVariantId,
        ReportDateRange range,
        CancellationToken cancellationToken = default) =>
        ReadAsync(VariantByIdSql, new { ProductVariantId = productVariantId }, range, cancellationToken);

    /// <inheritdoc />
    public Task<StockCard?> GetStockCardBySkuAsync(
        string skuOrBarcode,
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skuOrBarcode);
        return ReadAsync(VariantBySkuSql, new { Key = skuOrBarcode.Trim() }, range, cancellationToken);
    }

    private async Task<StockCard?> ReadAsync(
        string variantSql,
        object variantParameters,
        ReportDateRange range,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var variant = await connection.QuerySingleOrDefaultAsync<VariantRow>(
                new CommandDefinition(variantSql, variantParameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (variant is null)
            {
                return null;
            }

            var parameters = new
            {
                variant.ProductVariantId,
                From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ToExclusive = range.To.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };

            var movements = (await connection.QueryAsync<MovementRow>(
                new CommandDefinition(MovementsSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            var openingScaled = movements.Count > 0
                ? movements[0].PreviousBalanceAfterScaled ?? 0L
                : await connection.QuerySingleOrDefaultAsync<long?>(
                    new CommandDefinition(OpeningBeforeDateSql, parameters, cancellationToken: cancellationToken))
                    .ConfigureAwait(false) ?? 0L;

            return Build(variant, range, openingScaled, movements);
        }
    }

    private static StockCard Build(VariantRow variant, ReportDateRange range, long openingScaled, List<MovementRow> movements)
    {
        var uom = variant.BaseUomId;
        long totalIn = 0;
        long totalOut = 0;
        var everyRowMatches = true;
        var contiguous = true;
        long? shownAbove = null;
        var rows = new List<StockCardRow>(movements.Count);

        foreach (var movement in movements)
        {
            if (movement.QtyBaseScaled >= 0)
            {
                totalIn += movement.QtyBaseScaled;
            }
            else
            {
                totalOut += movement.QtyBaseScaled;
            }

            // The balance this movement should have left, built on the ledger's own previous row (zero before the
            // first ever movement), whether or not that row is in the date range.
            var running = (movement.PreviousBalanceAfterScaled ?? 0L) + movement.QtyBaseScaled;
            var matches = running == movement.BalanceAfterScaled;
            everyRowMatches &= matches;

            var occurredAt = ParseStamp(movement.OccurredAtText);
            var postedOutOfOrder = movement.PreviousOccurredAtText is { } previous && ParseStamp(previous) > occurredAt;
            var followsRowsNotShown = shownAbove is not null && movement.PreviousMovementId != shownAbove;
            contiguous &= !followsRowsNotShown;
            shownAbove = movement.MovementId;

            rows.Add(new StockCardRow(
                movement.MovementId,
                occurredAt,
                movement.MovementType,
                Quantity.FromScaled(movement.QtyBaseScaled, uom),
                Money.FromScaled(movement.UnitCostScaled),
                movement.RefDocType,
                movement.RefDocId,
                movement.ReferenceNo,
                movement.Note,
                Quantity.FromScaled(running, uom),
                Quantity.FromScaled(movement.BalanceAfterScaled, uom),
                matches,
                postedOutOfOrder,
                followsRowsNotShown));
        }

        var closingScaled = movements.Count == 0 ? openingScaled : movements[^1].BalanceAfterScaled;

        return new StockCard(
            variant.ProductVariantId,
            variant.Sku,
            variant.Description,
            variant.BaseUomSymbol,
            range,
            Quantity.FromScaled(openingScaled, uom),
            rows,
            Quantity.FromScaled(totalIn, uom),
            Quantity.FromScaled(totalOut, uom),
            Quantity.FromScaled(closingScaled, uom),
            everyRowMatches,
            contiguous);
    }

    private static DateTimeOffset ParseStamp(string text) =>
        DateTimeOffset.ParseExact(text, Iso8601Format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    private sealed class VariantRow
    {
        public long ProductVariantId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long BaseUomId { get; set; }

        public string BaseUomSymbol { get; set; } = string.Empty;
    }

    private sealed class MovementRow
    {
        public long MovementId { get; set; }

        public string OccurredAtText { get; set; } = string.Empty;

        public string MovementType { get; set; } = string.Empty;

        public long QtyBaseScaled { get; set; }

        public long UnitCostScaled { get; set; }

        public string RefDocType { get; set; } = string.Empty;

        public long? RefDocId { get; set; }

        public string ReferenceNo { get; set; } = string.Empty;

        public string? Note { get; set; }

        public long BalanceAfterScaled { get; set; }

        public long? PreviousMovementId { get; set; }

        public long? PreviousBalanceAfterScaled { get; set; }

        public string? PreviousOccurredAtText { get; set; }
    }
}
