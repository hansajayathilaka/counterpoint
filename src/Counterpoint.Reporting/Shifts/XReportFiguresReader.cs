using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Shifts;

/// <summary>
/// Answers <see cref="IXReportFiguresReader"/> off a report read connection (task P3-T02 "Do this" #1).
/// </summary>
/// <remarks>
/// <para>
/// Hand-written SQL over a read connection, not EF, reached through <see cref="IReportConnectionFactory"/>
/// because <c>Counterpoint.Reporting</c> may not reference <c>Counterpoint.Infrastructure</c>
/// (CLAUDE.md "Project boundaries") - the same seam <c>StockValuationQuery</c> and
/// <c>ReorderListQuery</c> already use.
/// </para>
/// <para>
/// Every query here is scoped to the one <c>shift_id</c> asked for, the same discipline
/// <c>SqliteCashMovementReader</c> keeps for cash: never a whole-table scan of <c>sale</c>,
/// <c>sale_return</c>, <c>sale_line</c> or <c>payment</c>. Only <c>sale.status = 'COMPLETED'</c>
/// rows count towards the sales-side figures - a cancelled sale never traded (the same predicate
/// <c>SqliteCashMovementReader.CashSalesSql</c> already applies for cash).
/// </para>
/// <para>
/// <b>Tender breakdown.</b> One <c>UNION ALL</c> over this shift's own sale payments and its own
/// return payments, tagged by source and grouped by <c>tender_type</c>: a refund's own
/// <c>payment.amount</c> is stored negative (docs/01_DATA_MODEL.md §5), so it is negated back to a
/// positive magnitude here, the same convention <c>SqliteCashMovementReader.CashRefundsSql</c>
/// already uses for cash refunds specifically.
/// </para>
/// </remarks>
internal sealed class XReportFiguresReader : IXReportFiguresReader
{
    private const string SalesTotalsSql =
        """
        SELECT COUNT(*) AS SalesCount,
               COALESCE(SUM(total), 0) AS SalesValueScaled,
               COALESCE(SUM(line_discount + bill_discount), 0) AS DiscountTotalScaled,
               COALESCE(SUM(tax), 0) AS SalesTaxTotalScaled
          FROM sale
         WHERE shift_id = @ShiftId
           AND status = 'COMPLETED';
        """;

    private const string ReturnsTotalsSql =
        """
        SELECT COUNT(*) AS ReturnsCount,
               COALESCE(SUM(total_refund), 0) AS ReturnsValueScaled,
               COALESCE(SUM(tax), 0) AS ReturnsTaxTotalScaled
          FROM sale_return
         WHERE shift_id = @ShiftId;
        """;

    // Line level, not a GROUP BY: a line's taxable amount is line_total less its share of the
    // bill discount (SaleReceiptFigures, BillDiscountSplit), and that share is a per-bill
    // allocation SQL cannot reproduce exactly. line_total is already net of tax in both pricing
    // modes, so it is never reduced by sl.tax. An exchange's replacement sale keeps
    // bill_discount at zero unconditionally (ExchangeTenderType0010) - its credit settles as an
    // EXCHANGE payment, never a discount - so the CASE below is defensive, not load-bearing; the
    // derived table finds those sales in one pass over sale_return rather than once per row.
    private const string TaxLinesSql =
        """
        SELECT sa.id AS SaleId,
               CASE WHEN ex.exchange_sale_id IS NULL THEN sa.bill_discount ELSE 0 END AS BillDiscountScaled,
               sl.qty AS QtyScaled, sl.uom_id AS UomId, sl.unit_price AS UnitPriceScaled, sl.discount AS DiscountScaled,
               sl.tax_rate AS TaxRateScaled, sl.line_total AS LineTotalScaled, sl.tax AS TaxScaled
          FROM sale_line sl
          JOIN sale sa ON sa.id = sl.sale_id
          LEFT JOIN (SELECT DISTINCT exchange_sale_id FROM sale_return WHERE exchange_sale_id IS NOT NULL) ex
            ON ex.exchange_sale_id = sa.id
         WHERE sa.shift_id = @ShiftId
           AND sa.status = 'COMPLETED'
         ORDER BY sa.id, sl.line_no;
        """;

    private const string TenderBreakdownSql =
        """
        SELECT tender_type AS TenderType,
               COALESCE(SUM(CASE WHEN source = 'SALE' THEN amount ELSE 0 END), 0) AS SalesAmountScaled,
               COALESCE(SUM(CASE WHEN source = 'RETURN' THEN -amount ELSE 0 END), 0) AS RefundsAmountScaled
          FROM (
                SELECT p.tender_type AS tender_type, p.amount AS amount, 'SALE' AS source
                  FROM payment p
                  JOIN sale sa ON sa.id = p.sale_id
                 WHERE sa.shift_id = @ShiftId
                   AND sa.status = 'COMPLETED'
                UNION ALL
                SELECT p.tender_type AS tender_type, p.amount AS amount, 'RETURN' AS source
                  FROM payment p
                  JOIN sale_return sr ON sr.id = p.sale_return_id
                 WHERE sr.shift_id = @ShiftId
               ) combined
         GROUP BY tender_type
         ORDER BY tender_type;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public XReportFiguresReader(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<XReportFigures> GetAsync(long shiftId, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var sales = await connection.QuerySingleAsync<SalesRow>(
                new CommandDefinition(SalesTotalsSql, new { ShiftId = shiftId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var returns = await connection.QuerySingleAsync<ReturnsRow>(
                new CommandDefinition(ReturnsTotalsSql, new { ShiftId = shiftId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var taxLines = await connection.QueryAsync<TaxLineRow>(
                new CommandDefinition(TaxLinesSql, new { ShiftId = shiftId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            var tenderRows = await connection.QueryAsync<TenderRow>(
                new CommandDefinition(TenderBreakdownSql, new { ShiftId = shiftId }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return new XReportFigures(
                sales.SalesCount,
                Money.FromScaled(sales.SalesValueScaled),
                Money.FromScaled(sales.DiscountTotalScaled),
                Money.FromScaled(sales.SalesTaxTotalScaled),
                returns.ReturnsCount,
                Money.FromScaled(returns.ReturnsValueScaled),
                Money.FromScaled(returns.ReturnsTaxTotalScaled),
                TaxBreakdown([.. taxLines]),
                [.. tenderRows.Select(ToTenderLine)]);
        }
    }

    private static IReadOnlyList<XReportTaxBreakdownLine> TaxBreakdown(IReadOnlyList<TaxLineRow> rows)
    {
        var taxable = new List<(long Rate, long Taxable, long Tax)>(rows.Count);

        foreach (var bill in rows.GroupBy(row => row.SaleId))
        {
            var lines = bill.ToList();
            var shares = BillDiscountSplit.Allocate(
                Money.FromScaled(lines[0].BillDiscountScaled),
                [.. lines.Select(line => BillDiscountSplit.Weight(
                    Money.FromScaled(line.UnitPriceScaled),
                    Quantity.FromScaled(line.QtyScaled, line.UomId),
                    Money.FromScaled(line.DiscountScaled)))]);

            for (var i = 0; i < lines.Count; i++)
            {
                taxable.Add((lines[i].TaxRateScaled, lines[i].LineTotalScaled - shares[i].ToScaled(), lines[i].TaxScaled));
            }
        }

        return [.. taxable
            .GroupBy(line => line.Rate)
            .OrderBy(group => group.Key)
            .Select(group => new XReportTaxBreakdownLine(
                TaxRate.FromScaled(group.Key),
                Money.FromScaled(group.Sum(line => line.Taxable)),
                Money.FromScaled(group.Sum(line => line.Tax))))];
    }

    private static XReportTenderLine ToTenderLine(TenderRow row) => new(
        row.TenderType,
        Money.FromScaled(row.SalesAmountScaled),
        Money.FromScaled(row.RefundsAmountScaled),
        Money.FromScaled(row.SalesAmountScaled - row.RefundsAmountScaled));

    /// <summary>The flat shape Dapper maps a row of <see cref="SalesTotalsSql"/> onto.</summary>
    private sealed class SalesRow
    {
        public int SalesCount { get; set; }

        public long SalesValueScaled { get; set; }

        public long DiscountTotalScaled { get; set; }

        public long SalesTaxTotalScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="ReturnsTotalsSql"/> onto.</summary>
    private sealed class ReturnsRow
    {
        public int ReturnsCount { get; set; }

        public long ReturnsValueScaled { get; set; }

        public long ReturnsTaxTotalScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="TaxLinesSql"/> onto.</summary>
    private sealed class TaxLineRow
    {
        public long SaleId { get; set; }

        public long BillDiscountScaled { get; set; }

        public long QtyScaled { get; set; }

        public long UomId { get; set; }

        public long UnitPriceScaled { get; set; }

        public long DiscountScaled { get; set; }

        public long TaxRateScaled { get; set; }

        public long LineTotalScaled { get; set; }

        public long TaxScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="TenderBreakdownSql"/> onto.</summary>
    private sealed class TenderRow
    {
        public string TenderType { get; set; } = string.Empty;

        public long SalesAmountScaled { get; set; }

        public long RefundsAmountScaled { get; set; }
    }
}
