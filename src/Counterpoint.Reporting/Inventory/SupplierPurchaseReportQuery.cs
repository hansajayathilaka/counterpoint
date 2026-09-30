using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Reporting.Queries;
using Dapper;

namespace Counterpoint.Reporting.Inventory;

/// <summary>
/// <see cref="ISupplierPurchaseReportQuery"/>: goods received by supplier and by item, with cost-price movement
/// (task P3-T06 "Do this" #5, SRS RPT-16).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b>, registered only wrapped with <c>RoleAuthorisation</c>.
/// </para>
/// <para>
/// By supplier is a grouped sum of <c>goods_receipt</c> header columns (addition only). By item and the cost
/// movement read the receipt lines row by row and do every average, difference and rate in C# - a weighted
/// average cost is <c>value / quantity</c>, never a scaled product in SQL, and nothing is rounded. Value is
/// landed cost excluding tax (<c>line_total - tax</c>, freight share included), which sums to the headers'
/// <c>subtotal + other_cost</c>. A receipt is dated by the wall-clock date at the front of <c>received_at</c>.
/// </para>
/// </remarks>
internal sealed class SupplierPurchaseReportQuery : ISupplierPurchaseReportQuery
{
    private const string Iso8601Format = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    private const string BySupplierSql =
        """
        SELECT gr.supplier_id AS SupplierId,
               s.name AS SupplierName,
               COUNT(*) AS ReceiptCount,
               COALESCE(SUM(gr.subtotal + gr.other_cost), 0) AS ValueScaled,
               COALESCE(SUM(gr.tax), 0) AS TaxScaled,
               COALESCE(SUM(gr.total), 0) AS TotalScaled
          FROM goods_receipt gr
          JOIN supplier s ON s.id = gr.supplier_id
         WHERE gr.received_at >= @From AND gr.received_at < @ToExclusive
           AND (@SupplierId IS NULL OR gr.supplier_id = @SupplierId)
         GROUP BY gr.supplier_id, s.name;
        """;

    private const string LinesSql =
        """
        SELECT grl.product_variant_id AS ProductVariantId,
               pv.sku AS Sku,
               p.name AS Description,
               p.base_uom_id AS BaseUomId,
               gr.received_at AS ReceivedAtText,
               gr.grn_no AS GrnNo,
               s.name AS SupplierName,
               grl.qty_base AS QtyBaseScaled,
               grl.unit_cost_base AS UnitCostBaseScaled,
               grl.line_total - grl.tax AS ValueScaled
          FROM goods_receipt_line grl
          JOIN goods_receipt gr ON gr.id = grl.goods_receipt_id
          JOIN supplier s ON s.id = gr.supplier_id
          JOIN product_variant pv ON pv.id = grl.product_variant_id
          JOIN product p ON p.id = pv.product_id
         WHERE gr.received_at >= @From AND gr.received_at < @ToExclusive
           AND (@SupplierId IS NULL OR gr.supplier_id = @SupplierId)
         ORDER BY gr.received_at, gr.id, grl.id;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public SupplierPurchaseReportQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<SupplierPurchaseReport> GetReportAsync(
        ReportDateRange range,
        long? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var parameters = new
        {
            From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ToExclusive = range.To.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            SupplierId = supplierId,
        };

        List<SupplierRow> supplierRows;
        List<LineRow> lines;

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            supplierRows = (await connection.QueryAsync<SupplierRow>(
                new CommandDefinition(BySupplierSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            lines = (await connection.QueryAsync<LineRow>(
                new CommandDefinition(LinesSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();
        }

        var bySupplier = supplierRows
            .Select(row => new SupplierPurchaseRow(
                row.SupplierId,
                row.SupplierName,
                row.ReceiptCount,
                Money.FromScaled(row.ValueScaled),
                Money.FromScaled(row.TaxScaled),
                Money.FromScaled(row.TotalScaled)))
            .OrderByDescending(row => row.Value)
            .ThenBy(row => row.SupplierName, StringComparer.Ordinal)
            .ToList();

        var byItem = new List<SupplierPurchaseItemRow>();
        var movement = new List<CostMovementPoint>();

        foreach (var item in lines.GroupBy(line => line.ProductVariantId))
        {
            var ordered = item.ToList();
            var value = CanonicalFigures.Sum(ordered.Select(line => Money.FromScaled(line.ValueScaled)));
            var qty = Quantity.FromScaled(ordered.Sum(line => line.QtyBaseScaled), ordered[0].BaseUomId);
            var first = Money.FromScaled(ordered[0].UnitCostBaseScaled);
            var last = Money.FromScaled(ordered[^1].UnitCostBaseScaled);
            var change = last - first;

            byItem.Add(new SupplierPurchaseItemRow(
                ordered[0].ProductVariantId,
                ordered[0].Sku,
                ordered[0].Description,
                qty,
                value,
                qty.IsZero ? Money.Zero : value / qty.Value,
                first,
                last,
                change,
                CanonicalFigures.Share(change, first)));

            // Only an item whose landed cost actually differed between receipts has a movement to show.
            if (ordered.Select(line => line.UnitCostBaseScaled).Distinct().Count() > 1)
            {
                movement.AddRange(ordered.Select(line => new CostMovementPoint(
                    line.ProductVariantId,
                    line.Sku,
                    DateTimeOffset.ParseExact(line.ReceivedAtText, Iso8601Format, CultureInfo.InvariantCulture, DateTimeStyles.None),
                    line.SupplierName,
                    line.GrnNo,
                    Money.FromScaled(line.UnitCostBaseScaled))));
            }
        }

        byItem = [.. byItem.OrderByDescending(row => row.Value).ThenBy(row => row.Sku, StringComparer.Ordinal)];
        movement = [.. movement.OrderBy(point => point.Sku, StringComparer.Ordinal).ThenBy(point => point.ReceivedAt)];

        return new SupplierPurchaseReport(
            range,
            supplierId,
            bySupplier,
            byItem,
            movement,
            CanonicalFigures.Sum(bySupplier.Select(row => row.Value)),
            CanonicalFigures.Sum(bySupplier.Select(row => row.Tax)),
            CanonicalFigures.Sum(bySupplier.Select(row => row.Total)));
    }

    private sealed class SupplierRow
    {
        public long SupplierId { get; set; }

        public string SupplierName { get; set; } = string.Empty;

        public int ReceiptCount { get; set; }

        public long ValueScaled { get; set; }

        public long TaxScaled { get; set; }

        public long TotalScaled { get; set; }
    }

    private sealed class LineRow
    {
        public long ProductVariantId { get; set; }

        public string Sku { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public long BaseUomId { get; set; }

        public string ReceivedAtText { get; set; } = string.Empty;

        public string GrnNo { get; set; } = string.Empty;

        public string SupplierName { get; set; } = string.Empty;

        public long QtyBaseScaled { get; set; }

        public long UnitCostBaseScaled { get; set; }

        public long ValueScaled { get; set; }
    }
}
