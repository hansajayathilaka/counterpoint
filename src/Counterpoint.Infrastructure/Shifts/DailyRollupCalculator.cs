using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Infrastructure.Shifts;

/// <summary>
/// Recomputes one business date's day and per-product totals from the raw <c>sale</c>,
/// <c>sale_line</c>, <c>sale_return</c> and <c>sale_return_line</c> tables - the single
/// implementation <see cref="SqliteDailyRollupBuilder"/> (writes) and
/// <see cref="SqliteRollupConsistencyCheck"/> (reads, never writes) both call, so the day rollup
/// and its own verification can never drift apart by using two different formulas (task P3-T03
/// "Do this" #2, the same "exactly one implementation" discipline
/// <c>Counterpoint.Domain.Cash.ExpectedCashCalculator</c> keeps for the expected-cash formula).
/// </summary>
/// <remarks>
/// <para>
/// <b>Money-safe multiplication, never raw SQL arithmetic.</b> <c>unit_cost × qty_base</c> is
/// computed in C# through <see cref="Money"/>'s own <c>decimal</c> multiplication, the identical
/// arithmetic <c>CompleteSaleHandler</c> uses to build <c>sale.cogs</c> in the first place
/// (<c>line.UnitCost * line.QuantityBase.Value</c>) - never <c>unit_cost_scaled * qty_base_scaled</c>
/// in SQL, which would double the storage scale and silently corrupt every cost figure this
/// produces.
/// </para>
/// <para>
/// <b>Gross, Discount and Net reconcile by construction.</b> <c>Gross</c> is the true pre-any-
/// discount figure (<c>subtotal + line_discount</c>); <c>Discount</c> is both levels together
/// (<c>line_discount + bill_discount</c>); <c>Net</c> is <c>Gross - Discount - ReturnSubtotal</c>,
/// which algebraically reduces to <c>subtotal - bill_discount - ReturnSubtotal</c> - the exact
/// canonical "gross - discounts - returns, excluding tax" shape task P3-T04 will formalise for the
/// wider report suite, computed once here rather than reinvented there.
/// </para>
/// </remarks>
internal static class DailyRollupCalculator
{
    private const string SalesHeaderSql =
        """
        SELECT
          COUNT(*) AS BillCount,
          COALESCE(SUM(subtotal), 0) AS SubtotalScaled,
          COALESCE(SUM(line_discount), 0) AS LineDiscountScaled,
          COALESCE(SUM(bill_discount), 0) AS BillDiscountScaled,
          COALESCE(SUM(tax), 0) AS TaxScaled,
          COALESCE(SUM(cogs), 0) AS CogsScaled
        FROM sale
        WHERE business_date = @BusinessDate AND status = 'COMPLETED';
        """;

    private const string ReturnsHeaderSql =
        """
        SELECT
          COUNT(*) AS ReturnCount,
          COALESCE(SUM(subtotal), 0) AS SubtotalScaled,
          COALESCE(SUM(total_refund), 0) AS TotalRefundScaled
        FROM sale_return
        WHERE business_date = @BusinessDate;
        """;

    private const string SaleLinesSql =
        """
        SELECT sl.product_variant_id AS ProductVariantId,
               sl.qty_base           AS QtyBaseScaled,
               sl.unit_cost          AS UnitCostScaled,
               sl.line_total         AS LineTotalScaled
          FROM sale_line sl
          JOIN sale sa ON sa.id = sl.sale_id
         WHERE sa.business_date = @BusinessDate
           AND sa.status = 'COMPLETED';
        """;

    private const string ReturnLinesSql =
        """
        SELECT srl.product_variant_id AS ProductVariantId,
               srl.qty_base           AS QtyBaseScaled,
               srl.unit_cost          AS UnitCostScaled,
               srl.line_refund        AS LineRefundScaled,
               srl.disposition        AS Disposition
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
         WHERE sr.business_date = @BusinessDate;
        """;

    private const string TenderBucketsSql =
        """
        SELECT
          CASE WHEN tender_type = 'CASH' THEN 'CASH'
               WHEN tender_type = 'CARD' THEN 'CARD'
               ELSE 'OTHER' END AS Bucket,
          COALESCE(SUM(CASE WHEN source = 'SALE' THEN amount ELSE 0 END), 0) AS SalesAmountScaled,
          COALESCE(SUM(CASE WHEN source = 'RETURN' THEN -amount ELSE 0 END), 0) AS RefundsAmountScaled
        FROM (
              SELECT p.tender_type AS tender_type, p.amount AS amount, 'SALE' AS source
                FROM payment p
                JOIN sale sa ON sa.id = p.sale_id
               WHERE sa.business_date = @BusinessDate
                 AND sa.status = 'COMPLETED'
              UNION ALL
              SELECT p.tender_type AS tender_type, p.amount AS amount, 'RETURN' AS source
                FROM payment p
                JOIN sale_return sr ON sr.id = p.sale_return_id
               WHERE sr.business_date = @BusinessDate
             ) combined
        GROUP BY Bucket;
        """;

    /// <summary>Recomputes the whole day, from the connection and (optional) transaction given.</summary>
    internal static async Task<DailyRollupComputation> ComputeAsync(
        DbConnection connection,
        DbTransaction? transaction,
        DateOnly businessDate,
        CancellationToken cancellationToken)
    {
        var dateText = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var parameters = new { BusinessDate = dateText };

        var salesHeader = await connection.QuerySingleAsync<SalesHeaderRow>(
            new CommandDefinition(SalesHeaderSql, parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var returnsHeader = await connection.QuerySingleAsync<ReturnsHeaderRow>(
            new CommandDefinition(ReturnsHeaderSql, parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var saleLines = await connection.QueryAsync<LineRow>(
            new CommandDefinition(SaleLinesSql, parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var returnLines = await connection.QueryAsync<ReturnLineRow>(
            new CommandDefinition(ReturnLinesSql, parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        var tenderBuckets = await connection.QueryAsync<TenderBucketRow>(
            new CommandDefinition(TenderBucketsSql, parameters, transaction, cancellationToken: cancellationToken))
            .ConfigureAwait(false);

        // The true gross, before even line-level discount - sale.subtotal is already net of it
        // (LineTaxCalculator's own remarks: "LineTotal is always the net figure").
        var gross = Money.FromScaled(salesHeader.SubtotalScaled) + Money.FromScaled(salesHeader.LineDiscountScaled);
        var discount = Money.FromScaled(salesHeader.LineDiscountScaled) + Money.FromScaled(salesHeader.BillDiscountScaled);
        var tax = Money.FromScaled(salesHeader.TaxScaled);
        var returnSubtotal = Money.FromScaled(returnsHeader.SubtotalScaled);

        // gross - discount - returnSubtotal, reduced algebraically (see remarks): the two
        // subtracted line_discount terms cancel, leaving subtotal - bill_discount - returnSubtotal.
        var net = gross - discount - returnSubtotal;

        var returnCogsByVariant = new Dictionary<long, Money>();
        var returnQtyByVariant = new Dictionary<long, long>();
        var returnNetByVariant = new Dictionary<long, Money>();
        var returnCogsTotal = Money.Zero;

        foreach (var line in returnLines)
        {
            // Only a SELLABLE line goes back onto the shelf (CreateReturnHandler posts a
            // RETURN_IN stock movement for that disposition alone) - only then is the cost
            // genuinely recovered and the sale's own COGS rightly reversed. A DAMAGED line never
            // re-enters stock and no write-off movement is posted for it either, so the shop has
            // both refunded the money and lost the goods; reversing its COGS here would erase
            // that loss and overstate the day's margin.
            var costRecovered = string.Equals(line.Disposition, ReturnDispositions.SellableToken, StringComparison.Ordinal);
            var lineCogs = costRecovered ? LineCogs(line.UnitCostScaled, line.QtyBaseScaled) : Money.Zero;
            returnCogsTotal += lineCogs;

            if (line.ProductVariantId is not { } variantId)
            {
                continue;
            }

            if (costRecovered)
            {
                Accumulate(returnCogsByVariant, variantId, lineCogs);
            }

            AccumulateScaled(returnQtyByVariant, variantId, line.QtyBaseScaled);
            Accumulate(returnNetByVariant, variantId, Money.FromScaled(line.LineRefundScaled));
        }

        var saleCogsByVariant = new Dictionary<long, Money>();
        var saleQtyByVariant = new Dictionary<long, long>();
        var saleNetByVariant = new Dictionary<long, Money>();

        foreach (var line in saleLines)
        {
            if (line.ProductVariantId is not { } variantId)
            {
                // Open-item lines (no product_variant_id) contribute to the day's own totals
                // through the sale header aggregate above, but there is no product to file a
                // per-product row against.
                continue;
            }

            Accumulate(saleCogsByVariant, variantId, LineCogs(line.UnitCostScaled, line.QtyBaseScaled));
            AccumulateScaled(saleQtyByVariant, variantId, line.QtyBaseScaled);
            Accumulate(saleNetByVariant, variantId, Money.FromScaled(line.LineTotalScaled));
        }

        var cogs = Money.FromScaled(salesHeader.CogsScaled) - returnCogsTotal;

        var variantIds = saleQtyByVariant.Keys.Union(returnQtyByVariant.Keys);
        var products = variantIds
            .Select(variantId => new DailyProductRollup(
                variantId,
                saleQtyByVariant.GetValueOrDefault(variantId) - returnQtyByVariant.GetValueOrDefault(variantId),
                saleNetByVariant.GetValueOrDefault(variantId, Money.Zero)
                    - returnNetByVariant.GetValueOrDefault(variantId, Money.Zero),
                saleCogsByVariant.GetValueOrDefault(variantId, Money.Zero)
                    - returnCogsByVariant.GetValueOrDefault(variantId, Money.Zero)))
            .OrderBy(product => product.ProductVariantId)
            .ToList();

        var tenderCash = Money.Zero;
        var tenderCard = Money.Zero;
        var tenderOther = Money.Zero;

        foreach (var bucket in tenderBuckets)
        {
            var bucketNet = Money.FromScaled(bucket.SalesAmountScaled) - Money.FromScaled(bucket.RefundsAmountScaled);

            switch (bucket.Bucket)
            {
                case "CASH":
                    tenderCash = bucketNet;
                    break;
                case "CARD":
                    tenderCard = bucketNet;
                    break;
                default:
                    tenderOther = bucketNet;
                    break;
            }
        }

        return new DailyRollupComputation(
            salesHeader.BillCount,
            gross,
            discount,
            tax,
            net,
            cogs,
            returnsHeader.ReturnCount,
            Money.FromScaled(returnsHeader.TotalRefundScaled),
            tenderCash,
            tenderCard,
            tenderOther,
            products);
    }

    /// <summary>
    /// <c>unit_cost × qty_base</c>, both as stored scaled integers, multiplied the same money-safe
    /// way <c>CompleteSaleHandler</c> computes <c>sale.cogs</c> - never a raw
    /// <c>scaled * scaled</c> in SQL, which double-scales.
    /// </summary>
    private static Money LineCogs(long unitCostScaled, long qtyBaseScaled) =>
        Money.FromScaled(unitCostScaled) * Quantity.FromScaled(qtyBaseScaled, uomId: 0).Value;

    private static void Accumulate(Dictionary<long, Money> totals, long key, Money amount) =>
        totals[key] = totals.TryGetValue(key, out var running) ? running + amount : amount;

    private static void AccumulateScaled(Dictionary<long, long> totals, long key, long amount) =>
        totals[key] = totals.TryGetValue(key, out var running) ? running + amount : amount;

    /// <summary>The flat shape Dapper maps a row of <see cref="SalesHeaderSql"/> onto.</summary>
    private sealed class SalesHeaderRow
    {
        public int BillCount { get; set; }

        public long SubtotalScaled { get; set; }

        public long LineDiscountScaled { get; set; }

        public long BillDiscountScaled { get; set; }

        public long TaxScaled { get; set; }

        public long CogsScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="ReturnsHeaderSql"/> onto.</summary>
    private sealed class ReturnsHeaderRow
    {
        public int ReturnCount { get; set; }

        public long SubtotalScaled { get; set; }

        public long TotalRefundScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="SaleLinesSql"/> onto.</summary>
    private sealed class LineRow
    {
        public long? ProductVariantId { get; set; }

        public long QtyBaseScaled { get; set; }

        public long UnitCostScaled { get; set; }

        public long LineTotalScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="ReturnLinesSql"/> onto.</summary>
    private sealed class ReturnLineRow
    {
        public long? ProductVariantId { get; set; }

        public long QtyBaseScaled { get; set; }

        public long UnitCostScaled { get; set; }

        public long LineRefundScaled { get; set; }

        public string Disposition { get; set; } = string.Empty;
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="TenderBucketsSql"/> onto.</summary>
    private sealed class TenderBucketRow
    {
        public string Bucket { get; set; } = string.Empty;

        public long SalesAmountScaled { get; set; }

        public long RefundsAmountScaled { get; set; }
    }
}

/// <summary>One business date's fully recomputed rollup (task P3-T03).</summary>
internal sealed record DailyRollupComputation(
    int BillCount,
    Money Gross,
    Money Discount,
    Money Tax,
    Money Net,
    Money Cogs,
    int ReturnCount,
    Money ReturnValue,
    Money TenderCash,
    Money TenderCard,
    Money TenderOther,
    IReadOnlyList<DailyProductRollup> Products);

/// <summary>One product variant's recomputed contribution to a business date (task P3-T03).</summary>
internal sealed record DailyProductRollup(long ProductVariantId, long QtyBaseScaled, Money Net, Money Cogs);
