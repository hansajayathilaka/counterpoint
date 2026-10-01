using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="ISalesBillQuery"/>: the bill list and single-bill views the sales reports drill into
/// (task P3-T05 "Do this" #5).
/// </summary>
/// <remarks>
/// Hand-written, fully parameterised SQL over a read connection. No cost column (<c>unit_cost</c>,
/// <c>cogs</c>) is selected anywhere in this class - the DTOs have nowhere to put one (CLAUDE.md
/// invariant 8). Every figure on a line is the snapshot stored on the row (invariant 10). The bill
/// list's gross/discount/net use the same <see cref="CanonicalFigures"/> formulas as the period
/// figures, so a day's bills add up to the day's pre-return net.
/// </remarks>
internal sealed class SalesBillQuery : ISalesBillQuery
{
    private const string BillListSql =
        """
        SELECT s.id AS SaleId, s.bill_no AS BillNo, s.sold_at AS SoldAt, s.business_date AS BusinessDate,
               COALESCE(c.name, 'Walk-in') AS CustomerName, u.display_name AS CashierName,
               s.subtotal AS SubtotalScaled, s.line_discount AS LineDiscountScaled,
               s.bill_discount AS BillDiscountScaled, s.tax AS TaxScaled, s.total AS TotalScaled,
               COALESCE((SELECT SUM(sr.subtotal) FROM sale_return sr WHERE sr.original_sale_id = s.id), 0)
                   AS ReturnedSubtotalScaled
          FROM sale s
          JOIN app_user u ON u.id = s.user_id
          LEFT JOIN customer c ON c.id = s.customer_id
         WHERE s.business_date >= @From AND s.business_date <= @To
           AND s.status = 'COMPLETED'
           AND (@Hour IS NULL OR CAST(substr(s.sold_at, 12, 2) AS INTEGER) = @Hour)
           AND (@ProductVariantId IS NULL
                OR EXISTS (SELECT 1 FROM sale_line sl
                            WHERE sl.sale_id = s.id AND sl.product_variant_id = @ProductVariantId))
         ORDER BY s.sold_at, s.id
         LIMIT @Limit;
        """;

    private const string BillHeaderSql =
        """
        SELECT s.id AS SaleId, s.bill_no AS BillNo, s.sold_at AS SoldAt, s.business_date AS BusinessDate,
               s.status AS Status, COALESCE(c.name, 'Walk-in') AS CustomerName, u.display_name AS CashierName,
               s.subtotal AS SubtotalScaled, s.line_discount AS LineDiscountScaled,
               s.bill_discount AS BillDiscountScaled, s.tax AS TaxScaled, s.rounding AS RoundingScaled,
               s.total AS TotalScaled, s.note AS Note
          FROM sale s
          JOIN app_user u ON u.id = s.user_id
          LEFT JOIN customer c ON c.id = s.customer_id
         WHERE s.id = @SaleId;
        """;

    private const string BillLinesSql =
        """
        SELECT sl.line_no AS LineNo, sl.description AS Description, sl.qty AS QtyScaled, sl.uom_id AS UomId,
               COALESCE(u.symbol, '') AS UomSymbol, sl.unit_price AS UnitPriceScaled,
               sl.discount AS DiscountScaled, sl.tax AS TaxScaled, sl.line_total AS LineTotalScaled,
               sl.qty_returned AS QtyReturnedScaled
          FROM sale_line sl
          LEFT JOIN uom u ON u.id = sl.uom_id
         WHERE sl.sale_id = @SaleId
         ORDER BY sl.line_no;
        """;

    private const string BillPaymentsSql =
        """
        SELECT tender_type AS TenderType, amount AS AmountScaled
          FROM payment
         WHERE sale_id = @SaleId
         ORDER BY id;
        """;

    private const string BillReturnsSql =
        """
        SELECT return_no AS ReturnNo, business_date AS BusinessDate, subtotal AS SubtotalScaled,
               total_refund AS TotalRefundScaled, refund_method AS RefundMethod
          FROM sale_return
         WHERE original_sale_id = @SaleId
         ORDER BY id;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public SalesBillQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<SalesBillList> GetBillsAsync(BillListFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(filter.MaxRows, 0);

        if (filter.Hour is < 0 or > 23)
        {
            throw new ArgumentOutOfRangeException(nameof(filter), filter.Hour, "The hour of day must be 0 to 23.");
        }

        var parameters = new
        {
            From = Text(filter.Range.From),
            To = Text(filter.Range.To),
            filter.Hour,
            filter.ProductVariantId,

            // One more than asked for, so the caller can be told the list was cut short.
            Limit = filter.MaxRows + 1,
        };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = (await connection.QueryAsync<BillRow>(
                new CommandDefinition(BillListSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            var truncated = rows.Count > filter.MaxRows;

            return new SalesBillList([.. rows.Take(filter.MaxRows).Select(ToBillRow)], truncated);
        }
    }

    /// <inheritdoc />
    public async Task<SalesBillDetail?> GetBillAsync(long saleId, CancellationToken cancellationToken = default)
    {
        var parameters = new { SaleId = saleId };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var header = await connection.QuerySingleOrDefaultAsync<HeaderRow>(
                new CommandDefinition(BillHeaderSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            if (header is null)
            {
                return null;
            }

            var lines = await connection.QueryAsync<LineRow>(
                new CommandDefinition(BillLinesSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            var payments = await connection.QueryAsync<PaymentRow>(
                new CommandDefinition(BillPaymentsSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            var returns = await connection.QueryAsync<ReturnRow>(
                new CommandDefinition(BillReturnsSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            return new SalesBillDetail(
                header.SaleId,
                header.BillNo,
                DateTimeOffset.Parse(header.SoldAt, CultureInfo.InvariantCulture),
                ParseDate(header.BusinessDate),
                header.Status,
                header.CashierName,
                header.CustomerName,
                Money.FromScaled(header.SubtotalScaled),
                Money.FromScaled(header.LineDiscountScaled),
                Money.FromScaled(header.BillDiscountScaled),
                Money.FromScaled(header.TaxScaled),
                Money.FromScaled(header.RoundingScaled),
                Money.FromScaled(header.TotalScaled),
                header.Note,
                [
                    .. lines.Select(line => new SalesBillLine(
                        line.LineNo,
                        line.Description,
                        Quantity.FromScaled(line.QtyScaled, line.UomId),
                        line.UomSymbol,
                        Money.FromScaled(line.UnitPriceScaled),
                        Money.FromScaled(line.DiscountScaled),
                        Money.FromScaled(line.TaxScaled),
                        Money.FromScaled(line.LineTotalScaled),
                        Quantity.FromScaled(line.QtyReturnedScaled, uomId: 0))),
                ],
                [.. payments.Select(payment => new SalesBillPayment(payment.TenderType, Money.FromScaled(payment.AmountScaled)))],
                [
                    .. returns.Select(ret => new SalesBillReturn(
                        ret.ReturnNo,
                        ParseDate(ret.BusinessDate),
                        Money.FromScaled(ret.SubtotalScaled),
                        Money.FromScaled(ret.TotalRefundScaled),
                        ret.RefundMethod)),
                ]);
        }
    }

    private static SalesBillRow ToBillRow(BillRow row)
    {
        var gross = CanonicalFigures.Gross(row.SubtotalScaled, row.LineDiscountScaled);
        var discounts = CanonicalFigures.Discounts(row.LineDiscountScaled, row.BillDiscountScaled);

        return new SalesBillRow(
            row.SaleId,
            row.BillNo,
            DateTimeOffset.Parse(row.SoldAt, CultureInfo.InvariantCulture),
            ParseDate(row.BusinessDate),
            row.CustomerName,
            row.CashierName,
            gross,
            discounts,
            Money.FromScaled(row.TaxScaled),
            gross - discounts,
            Money.FromScaled(row.TotalScaled),
            Money.FromScaled(row.ReturnedSubtotalScaled));
    }

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class BillRow
    {
        public long SaleId { get; set; }

        public string BillNo { get; set; } = string.Empty;

        public string SoldAt { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public string CashierName { get; set; } = string.Empty;

        public long SubtotalScaled { get; set; }

        public long LineDiscountScaled { get; set; }

        public long BillDiscountScaled { get; set; }

        public long TaxScaled { get; set; }

        public long TotalScaled { get; set; }

        public long ReturnedSubtotalScaled { get; set; }
    }

    private sealed class HeaderRow
    {
        public long SaleId { get; set; }

        public string BillNo { get; set; } = string.Empty;

        public string SoldAt { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string CustomerName { get; set; } = string.Empty;

        public string CashierName { get; set; } = string.Empty;

        public long SubtotalScaled { get; set; }

        public long LineDiscountScaled { get; set; }

        public long BillDiscountScaled { get; set; }

        public long TaxScaled { get; set; }

        public long RoundingScaled { get; set; }

        public long TotalScaled { get; set; }

        public string? Note { get; set; }
    }

    private sealed class LineRow
    {
        public int LineNo { get; set; }

        public string Description { get; set; } = string.Empty;

        public long QtyScaled { get; set; }

        public long UomId { get; set; }

        public string UomSymbol { get; set; } = string.Empty;

        public long UnitPriceScaled { get; set; }

        public long DiscountScaled { get; set; }

        public long TaxScaled { get; set; }

        public long LineTotalScaled { get; set; }

        public long QtyReturnedScaled { get; set; }
    }

    private sealed class PaymentRow
    {
        public string TenderType { get; set; } = string.Empty;

        public long AmountScaled { get; set; }
    }

    private sealed class ReturnRow
    {
        public string ReturnNo { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public long SubtotalScaled { get; set; }

        public long TotalRefundScaled { get; set; }

        public string RefundMethod { get; set; } = string.Empty;
    }
}
