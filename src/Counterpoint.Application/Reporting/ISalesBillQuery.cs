using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// The drill-down half of the sales reports: a summary row leads to a bill list, a bill-list row
/// leads to one bill (task P3-T05 "Do this" #5).
/// </summary>
/// <remarks>
/// Not owner-only, and cost-free: neither <see cref="SalesBillRow"/> nor <see cref="SalesBillDetail"/>
/// has a cost or margin field (CLAUDE.md invariant 8). A bill shows the snapshot description, price
/// and discount it was sold with (CLAUDE.md invariant 10), never the catalogue's current ones.
/// </remarks>
public interface ISalesBillQuery
{
    /// <summary>The completed bills matching <paramref name="filter"/>, oldest first.</summary>
    public Task<SalesBillList> GetBillsAsync(BillListFilter filter, CancellationToken cancellationToken = default);

    /// <summary>One bill in full, or null when no such bill exists.</summary>
    public Task<SalesBillDetail?> GetBillAsync(long saleId, CancellationToken cancellationToken = default);
}

/// <summary>Which bills a drill-down lists.</summary>
/// <param name="Range">The inclusive business-date range (one day for a by-day drill-down).</param>
/// <param name="Hour">When set, only bills sold in this hour of day (0-23) - the by-hour drill-down.</param>
/// <param name="ProductVariantId">When set, only bills with a line for this product variant - the by-item drill-down.</param>
/// <param name="MaxRows">The most rows to return; the list reports when it was cut short.</param>
public sealed record BillListFilter(
    ReportDateRange Range,
    int? Hour = null,
    long? ProductVariantId = null,
    int MaxRows = BillListFilter.DefaultMaxRows)
{
    /// <summary>The default cap on a bill list, so a one-year drill-down stays responsive.</summary>
    public const int DefaultMaxRows = 2000;
}

/// <summary>A bill list and whether it was cut short by <see cref="BillListFilter.MaxRows"/>.</summary>
/// <param name="Rows">The bills, oldest first.</param>
/// <param name="IsTruncated">True when more bills matched than <see cref="Rows"/> holds.</param>
public sealed record SalesBillList(IReadOnlyList<SalesBillRow> Rows, bool IsTruncated);

/// <summary>One bill in a drill-down list.</summary>
/// <param name="SaleId">The bill's row id, the key <see cref="ISalesBillQuery.GetBillAsync"/> takes.</param>
/// <param name="BillNo">The bill number.</param>
/// <param name="SoldAt">When it was completed.</param>
/// <param name="BusinessDate">Its business date.</param>
/// <param name="CustomerName">The customer, or "Walk-in".</param>
/// <param name="CashierName">Who sold it.</param>
/// <param name="Gross">Before any discount, excluding tax (<c>subtotal + line_discount</c>).</param>
/// <param name="Discounts">Line plus bill discount.</param>
/// <param name="Tax">Tax charged.</param>
/// <param name="Net">Gross minus discounts, excluding tax and before any return against it.</param>
/// <param name="Total">What the customer was charged, including tax and rounding.</param>
/// <param name="ReturnedSubtotal">Pre-tax value of returns taken against this bill (any business date).</param>
public sealed record SalesBillRow(
    long SaleId,
    string BillNo,
    DateTimeOffset SoldAt,
    DateOnly BusinessDate,
    string CustomerName,
    string CashierName,
    Money Gross,
    Money Discounts,
    Money Tax,
    Money Net,
    Money Total,
    Money ReturnedSubtotal);

/// <summary>One bill in full.</summary>
/// <param name="SaleId">The bill's row id.</param>
/// <param name="BillNo">The bill number.</param>
/// <param name="SoldAt">When it was completed.</param>
/// <param name="BusinessDate">Its business date.</param>
/// <param name="Status"><c>COMPLETED</c> or <c>CANCELLED</c>.</param>
/// <param name="CashierName">Who sold it.</param>
/// <param name="CustomerName">The customer, or "Walk-in".</param>
/// <param name="Subtotal"><c>sale.subtotal</c>, net of line discounts and of tax.</param>
/// <param name="LineDiscount">The sum of the line discounts.</param>
/// <param name="BillDiscount">The bill-level discount.</param>
/// <param name="Tax">Tax charged.</param>
/// <param name="Rounding">The bill-total rounding adjustment.</param>
/// <param name="Total">What the customer was charged.</param>
/// <param name="Note">The bill's note, if any.</param>
/// <param name="Lines">The lines as sold.</param>
/// <param name="Payments">The tenders applied.</param>
/// <param name="Returns">Returns taken against this bill.</param>
public sealed record SalesBillDetail(
    long SaleId,
    string BillNo,
    DateTimeOffset SoldAt,
    DateOnly BusinessDate,
    string Status,
    string CashierName,
    string CustomerName,
    Money Subtotal,
    Money LineDiscount,
    Money BillDiscount,
    Money Tax,
    Money Rounding,
    Money Total,
    string? Note,
    IReadOnlyList<SalesBillLine> Lines,
    IReadOnlyList<SalesBillPayment> Payments,
    IReadOnlyList<SalesBillReturn> Returns);

/// <summary>One line of a bill, as snapshotted when it was sold.</summary>
/// <param name="LineNo">The line's position on the bill.</param>
/// <param name="Description">The description as sold.</param>
/// <param name="Quantity">Quantity in the selling unit.</param>
/// <param name="UomSymbol">The selling unit's symbol.</param>
/// <param name="UnitPrice">Price per selling unit as charged.</param>
/// <param name="Discount">The line discount.</param>
/// <param name="Tax">The line's tax.</param>
/// <param name="LineTotal"><c>sale_line.line_total</c>.</param>
/// <param name="QuantityReturnedBase">How much of this line has been returned, in base units.</param>
public sealed record SalesBillLine(
    int LineNo,
    string Description,
    Quantity Quantity,
    string UomSymbol,
    Money UnitPrice,
    Money Discount,
    Money Tax,
    Money LineTotal,
    Quantity QuantityReturnedBase);

/// <summary>One tender applied to a bill.</summary>
/// <param name="TenderType">The <c>payment.tender_type</c> token.</param>
/// <param name="Amount">The amount tendered.</param>
public sealed record SalesBillPayment(string TenderType, Money Amount);

/// <summary>A return taken against a bill.</summary>
/// <param name="ReturnNo">The return's document number.</param>
/// <param name="BusinessDate">The return's own business date.</param>
/// <param name="Subtotal">Pre-tax value returned.</param>
/// <param name="TotalRefund">What was refunded.</param>
/// <param name="RefundMethod">How it was refunded.</param>
public sealed record SalesBillReturn(
    string ReturnNo,
    DateOnly BusinessDate,
    Money Subtotal,
    Money TotalRefund,
    string RefundMethod);
