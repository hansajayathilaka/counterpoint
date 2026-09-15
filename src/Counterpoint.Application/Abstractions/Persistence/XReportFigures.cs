using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The raw sales, returns, tax and tender figures for one shift, scoped to that shift alone
/// (task P3-T02 "Do this" #1, SRS FR-8.3, RPT-04).
/// </summary>
/// <param name="SalesCount">Completed sales rung up this shift (<c>sale.status = 'COMPLETED'</c>).</param>
/// <param name="SalesValue">The sum of <c>sale.total</c> for those sales.</param>
/// <param name="DiscountTotal">The sum of <c>sale.line_discount + sale.bill_discount</c> for those sales.</param>
/// <param name="SalesTaxTotal">The sum of <c>sale.tax</c> for those sales.</param>
/// <param name="ReturnsCount">Returns taken this shift (<c>sale_return.shift_id</c>, any status - a return is never cancelled).</param>
/// <param name="ReturnsValue">The sum of <c>sale_return.total_refund</c> for those returns.</param>
/// <param name="ReturnsTaxTotal">The sum of <c>sale_return.tax</c> for those returns.</param>
/// <param name="TaxBreakdown">
/// This shift's completed sales broken down by <c>sale_line.tax_rate</c>, highest rate last. A
/// return's own tax has no stored rate to break down by (<c>sale_return_line</c> carries only the
/// amount), so it is not part of this breakdown - it is netted, as a single figure, into
/// <see cref="ReturnsTaxTotal"/> above.
/// </param>
/// <param name="Tenders">This shift's own payments, sale and refund, grouped by tender type.</param>
public sealed record XReportFigures(
    int SalesCount,
    Money SalesValue,
    Money DiscountTotal,
    Money SalesTaxTotal,
    int ReturnsCount,
    Money ReturnsValue,
    Money ReturnsTaxTotal,
    IReadOnlyList<XReportTaxBreakdownLine> TaxBreakdown,
    IReadOnlyList<XReportTenderLine> Tenders);

/// <summary>One <c>sale_line.tax_rate</c> bracket's contribution to this shift's completed sales.</summary>
/// <param name="Rate">The rate, for example 15% (<c>TaxRate.FromPercent(15)</c>).</param>
/// <param name="TaxableAmount">The net amount taxed at this rate (<c>line_total - tax</c>, summed).</param>
/// <param name="TaxAmount">The tax collected at this rate.</param>
public sealed record XReportTaxBreakdownLine(TaxRate Rate, Money TaxableAmount, Money TaxAmount);

/// <summary>One <c>payment.tender_type</c>'s movement this shift, sale and refund shown separately.</summary>
/// <param name="TenderType">One of the values <c>TenderTypes</c> (Application.Sales) spells out.</param>
/// <param name="SalesAmount">The sum tendered this way on this shift's own completed sales.</param>
/// <param name="RefundsAmount">The sum refunded this way on this shift's own returns, as a positive magnitude.</param>
/// <param name="NetAmount"><see cref="SalesAmount"/> minus <see cref="RefundsAmount"/>.</param>
public sealed record XReportTenderLine(string TenderType, Money SalesAmount, Money RefundsAmount, Money NetAmount);
