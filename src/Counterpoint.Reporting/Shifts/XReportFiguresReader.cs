using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
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

    private const string TaxBreakdownSql =
        """
        SELECT sl.tax_rate AS TaxRateScaled,
               COALESCE(SUM(sl.line_total - sl.tax), 0) AS TaxableAmountScaled,
               COALESCE(SUM(sl.tax), 0) AS TaxAmountScaled
          FROM sale_line sl
          JOIN sale sa ON sa.id = sl.sale_id
         WHERE sa.shift_id = @ShiftId
           AND sa.status = 'COMPLETED'
         GROUP BY sl.tax_rate
         ORDER BY sl.tax_rate;
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

            var taxRows = await connection.QueryAsync<TaxRow>(
                new CommandDefinition(TaxBreakdownSql, new { ShiftId = shiftId }, cancellationToken: cancellationToken))
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
                [.. taxRows.Select(ToTaxLine)],
                [.. tenderRows.Select(ToTenderLine)]);
        }
    }

    private static XReportTaxBreakdownLine ToTaxLine(TaxRow row) => new(
        TaxRate.FromScaled(row.TaxRateScaled),
        Money.FromScaled(row.TaxableAmountScaled),
        Money.FromScaled(row.TaxAmountScaled));

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

    /// <summary>The flat shape Dapper maps a row of <see cref="TaxBreakdownSql"/> onto.</summary>
    private sealed class TaxRow
    {
        public long TaxRateScaled { get; set; }

        public long TaxableAmountScaled { get; set; }

        public long TaxAmountScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="TenderBreakdownSql"/> onto.</summary>
    private sealed class TenderRow
    {
        public string TenderType { get; set; } = string.Empty;

        public long SalesAmountScaled { get; set; }

        public long RefundsAmountScaled { get; set; }
    }
}
