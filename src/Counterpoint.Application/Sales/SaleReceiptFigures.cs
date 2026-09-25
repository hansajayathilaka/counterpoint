using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// One bill line, as the receipt's arithmetic needs it.
/// </summary>
/// <param name="Description">The snapshot description (CLAUDE.md invariant 10).</param>
/// <param name="Quantity">Quantity in the selling unit.</param>
/// <param name="UomSymbol">The selling unit's symbol.</param>
/// <param name="UnitPrice">Price per selling unit, as shelf-priced.</param>
/// <param name="Charged">
/// What the line comes to after its own line discount - the "Amount" column. Gross of tax in an
/// inclusive shop, net in an exclusive one; either way it is what the customer reads beside
/// quantity and rate.
/// </param>
/// <param name="LineTotal"><c>sale_line.line_total</c>.</param>
/// <param name="Tax"><c>sale_line.tax</c>.</param>
/// <param name="TaxRate"><c>sale_line.tax_rate</c>.</param>
/// <param name="BillDiscountShare">This line's share of the bill discount (<c>BillDiscountSplit</c>).</param>
public sealed record SaleReceiptLineFigures(
    string Description,
    Quantity Quantity,
    string UomSymbol,
    Money UnitPrice,
    Money Charged,
    Money LineTotal,
    Money Tax,
    TaxRate TaxRate,
    Money BillDiscountShare);

/// <summary>
/// The one place a sale receipt's totals block is derived (SRS §10.1), for the original print and
/// for a reprint alike, so the two can never disagree.
/// </summary>
/// <remarks>
/// <para>
/// The SRS §10.1 block reads top to bottom as arithmetic, and it has to add up:
/// <c>Sub total - Discount = Taxable value (+ Tax) = TOTAL</c>.
/// </para>
/// <list type="bullet">
/// <item><b>Sub total</b> is the sum of the printed line amounts. Those already have their own
/// line discounts taken off, so the <b>Discount</b> row is the bill discount alone - adding line
/// discounts to it again would subtract them twice.</item>
/// <item><b>Taxable value</b> is <c>sale.subtotal - sale.bill_discount</c>. In an exclusive shop
/// that is the sub total less the discount; in an inclusive one it is also net of the tax carved
/// out of it. The header identity makes both the same expression.</item>
/// <item>Each tax row's taxable amount is <c>line_total - bill discount share</c>, summed per rate.</item>
/// </list>
/// </remarks>
public static class SaleReceiptFigures
{
    public static SaleReceipt Build(
        string billNo,
        DateTimeOffset soldAt,
        IReadOnlyList<SaleReceiptLineFigures> lines,
        Money subtotal,
        Money billDiscount,
        Money tax,
        Money total,
        IReadOnlyList<SaleReceiptTender> tenders,
        Money change,
        string taxLabel,
        string cashierName,
        string customerName,
        bool isTradeCustomer)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var printedSubtotal = Money.FromScaled(lines.Sum(line => line.Charged.ToScaled()));

        return new SaleReceipt(
            billNo,
            soldAt,
            [.. lines.Select(line => new SaleReceiptLine(
                line.Description,
                line.Quantity,
                line.UomSymbol,
                line.UnitPrice,
                line.Charged))],
            printedSubtotal,
            billDiscount,
            subtotal - billDiscount,
            tax,
            total,
            tenders,
            change,
            [.. lines
                .GroupBy(line => line.TaxRate)
                .OrderByDescending(group => group.Key.Rate)
                .Select(group => new SaleReceiptTaxLine(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{taxLabel} @ {group.Key.AsPercent:0.##}%"),
                    Money.FromScaled(group.Sum(line => line.LineTotal.ToScaled() - line.BillDiscountShare.ToScaled())),
                    Money.FromScaled(group.Sum(line => line.Tax.ToScaled()))))],
            cashierName,
            customerName,
            isTradeCustomer);
    }
}
