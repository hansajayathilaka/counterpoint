using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Sales;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Sales;

/// <summary>
/// Reassembles a completed bill from <c>sale</c>, <c>sale_line</c> and <c>payment</c>, for a
/// reprint (SRS FR-3.36, FR-7.5), off a read connection.
/// </summary>
/// <remarks>
/// Three queries on the one connection, all against append-only tables (<c>sale_line.qty_returned</c>
/// is the one exception, and this never reads it), so nothing here can be invalidated by anything
/// that happens between the read and the print job it feeds.
/// </remarks>
internal sealed class SqliteSaleReceiptLookup : ISaleReceiptLookup
{
    private const string SaleSql =
        """
        SELECT s.id AS Id, s.bill_no AS BillNo, s.sold_at AS SoldAt, s.subtotal AS Subtotal,
               s.line_discount AS LineDiscount, s.bill_discount AS BillDiscount,
               s.tax AS Tax, s.total AS Total,
               u.display_name AS CashierName,
               c.name AS CustomerName, c.type AS CustomerType,
               EXISTS (SELECT 1 FROM sale_return r WHERE r.exchange_sale_id = s.id) AS IsExchangeSale
          FROM sale s
          JOIN app_user u ON u.id = s.user_id
          LEFT JOIN customer c ON c.id = s.customer_id
         WHERE s.id = @SaleId
         LIMIT 1;
        """;

    private const string LinesSql =
        """
        SELECT sl.description AS Description, sl.qty AS Qty, sl.uom_id AS UomId,
               uo.symbol AS UomSymbol, sl.unit_price AS UnitPrice, sl.discount AS Discount,
               sl.line_total AS LineTotal, sl.tax_rate AS TaxRate, sl.tax AS Tax
          FROM sale_line sl
          JOIN uom uo ON uo.id = sl.uom_id
         WHERE sl.sale_id = @SaleId
         ORDER BY sl.line_no;
        """;

    private const string PaymentsSql =
        """
        SELECT tender_type AS TenderType, amount AS Amount
          FROM payment
         WHERE sale_id = @SaleId
         ORDER BY id;
        """;

    private readonly IPosConnectionFactory _connectionFactory;
    private readonly IRoundingPolicy _rounding;

    public SqliteSaleReceiptLookup(IPosConnectionFactory connectionFactory, IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(rounding);
        _connectionFactory = connectionFactory;
        _rounding = rounding;
    }

    /// <inheritdoc />
    public async Task<SaleReceipt?> FindReceiptAsync(long saleId, CancellationToken cancellationToken = default)
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

            var linesCommand = new CommandDefinition(LinesSql, new { SaleId = saleId }, cancellationToken: cancellationToken);
            var lines = await connection.QueryAsync<LineRow>(linesCommand).ConfigureAwait(false);

            var paymentsCommand = new CommandDefinition(PaymentsSql, new { SaleId = saleId }, cancellationToken: cancellationToken);
            var payments = await connection.QueryAsync<PaymentRow>(paymentsCommand).ConfigureAwait(false);

            return ToReceipt(sale, [.. lines], [.. payments]);
        }
    }

    private SaleReceipt ToReceipt(SaleRow sale, IReadOnlyList<LineRow> lines, IReadOnlyList<PaymentRow> payments)
    {
        // An exchange's replacement sale carries its return credit in bill_discount
        // (CreateExchangeHandler) - settlement, not a discount, and never split into the lines'
        // tax base - so it has nothing to allocate.
        var billDiscount = Money.FromScaled(sale.BillDiscount);
        var shares = BillDiscountSplit.Allocate(
            sale.IsExchangeSale ? Money.Zero : billDiscount,
            [.. lines.Select(line => BillDiscountSplit.Weight(
                Money.FromScaled(line.UnitPrice),
                Quantity.FromScaled(line.Qty, line.UomId),
                Money.FromScaled(line.Discount)))]);

        return SaleReceiptFigures.Build(
            sale.BillNo,
            DateTimeOffset.Parse(sale.SoldAt, CultureInfo.InvariantCulture),
            [.. lines.Select((line, i) => ToLine(line, shares[i]))],
            Money.FromScaled(sale.Subtotal),
            billDiscount,
            Money.FromScaled(sale.Tax),
            Money.FromScaled(sale.Total),
            [.. payments.Select(payment => new SaleReceiptTender(payment.TenderType, Money.FromScaled(payment.Amount)))],
            // Never recoverable from stored rows (SRS FR-3.26) - see the interface's remarks.
            Money.Zero,
            "Tax",
            sale.CashierName,
            sale.CustomerName ?? "Walk-in",
            string.Equals(sale.CustomerType, CustomerPriceTiers.TradeToken, StringComparison.Ordinal));
    }

    /// <summary>
    /// The line as the original receipt printed it. Its "Amount" is recomputed exactly as the sale
    /// path computed it - rounding point one over the three snapshot columns - because the stored
    /// <c>line_total</c> is net of tax in a tax-inclusive shop and so is not what was charged.
    /// </summary>
    private SaleReceiptLineFigures ToLine(LineRow row, Money billDiscountShare)
    {
        var quantity = Quantity.FromScaled(row.Qty, row.UomId);
        var unitPrice = Money.FromScaled(row.UnitPrice);

        return new SaleReceiptLineFigures(
            row.Description,
            quantity,
            row.UomSymbol,
            unitPrice,
            _rounding.Round((unitPrice * quantity.Value) - Money.FromScaled(row.Discount)),
            Money.FromScaled(row.LineTotal),
            Money.FromScaled(row.Tax),
            TaxRate.FromScaled(row.TaxRate),
            billDiscountShare);
    }

    private sealed class SaleRow
    {
        public long Id { get; set; }

        public string BillNo { get; set; } = string.Empty;

        public string SoldAt { get; set; } = string.Empty;

        public long Subtotal { get; set; }

        public long LineDiscount { get; set; }

        public long BillDiscount { get; set; }

        public long Tax { get; set; }

        public long Total { get; set; }

        public string CashierName { get; set; } = string.Empty;

        public string? CustomerName { get; set; }

        public string? CustomerType { get; set; }

        public bool IsExchangeSale { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="LinesSql"/> onto.</summary>
    private sealed class LineRow
    {
        public string Description { get; set; } = string.Empty;

        public long Qty { get; set; }

        public long UomId { get; set; }

        public string UomSymbol { get; set; } = string.Empty;

        public long UnitPrice { get; set; }

        public long Discount { get; set; }

        public long LineTotal { get; set; }

        public long TaxRate { get; set; }

        public long Tax { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="PaymentsSql"/> onto.</summary>
    private sealed class PaymentRow
    {
        public string TenderType { get; set; } = string.Empty;

        public long Amount { get; set; }
    }
}
