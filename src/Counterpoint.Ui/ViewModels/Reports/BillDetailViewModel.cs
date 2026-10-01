using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>One bill in full - a display-ready projection of <see cref="SalesBillDetail"/>.</summary>
public sealed class BillDetailViewModel
{
    public BillDetailViewModel(SalesBillDetail detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        BillNo = detail.BillNo;
        HeadingText = "Bill " + detail.BillNo + (detail.Status == "CANCELLED" ? " (cancelled)" : string.Empty);
        SoldAtText = detail.SoldAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        CashierText = "Cashier: " + detail.CashierName;
        CustomerText = "Customer: " + detail.CustomerName;
        SubtotalText = ReportText.Money(detail.Subtotal);
        LineDiscountText = ReportText.Money(detail.LineDiscount);
        BillDiscountText = ReportText.Money(detail.BillDiscount);
        TaxText = ReportText.Money(detail.Tax);
        RoundingText = ReportText.Money(detail.Rounding);
        TotalText = ReportText.Money(detail.Total);
        NoteText = detail.Note ?? string.Empty;
        Lines =
        [
            .. detail.Lines.Select(line => new BillLineRow(
                line.LineNo.ToString(CultureInfo.InvariantCulture),
                line.Description,
                ReportText.Quantity(line.Quantity, line.UomSymbol),
                ReportText.Money(line.UnitPrice),
                ReportText.Money(line.Discount),
                ReportText.Money(line.Tax),
                ReportText.Money(line.LineTotal),
                line.QuantityReturnedBase.IsZero
                    ? string.Empty
                    : ReportText.Quantity(line.QuantityReturnedBase, uomSymbol: null) + " returned")),
        ];
        Payments = [.. detail.Payments.Select(payment => new BillPaymentRow(payment.TenderType, ReportText.Money(payment.Amount)))];
        Returns =
        [
            .. detail.Returns.Select(ret => new BillReturnRow(
                ret.ReturnNo,
                ReportText.Date(ret.BusinessDate),
                ret.RefundMethod,
                ReportText.Money(ret.TotalRefund))),
        ];
        HasReturns = Returns.Count > 0;
    }

    public string BillNo { get; }

    public string HeadingText { get; }

    public string SoldAtText { get; }

    public string CashierText { get; }

    public string CustomerText { get; }

    public string SubtotalText { get; }

    public string LineDiscountText { get; }

    public string BillDiscountText { get; }

    public string TaxText { get; }

    public string RoundingText { get; }

    public string TotalText { get; }

    public string NoteText { get; }

    public IReadOnlyList<BillLineRow> Lines { get; }

    public IReadOnlyList<BillPaymentRow> Payments { get; }

    public IReadOnlyList<BillReturnRow> Returns { get; }

    public bool HasReturns { get; }
}

/// <summary>One line of the bill view.</summary>
public sealed record BillLineRow(
    string LineNoText,
    string Description,
    string QuantityText,
    string UnitPriceText,
    string DiscountText,
    string TaxText,
    string LineTotalText,
    string ReturnedText);

/// <summary>One tender of the bill view.</summary>
public sealed record BillPaymentRow(string TenderType, string AmountText);

/// <summary>One return against the bill.</summary>
public sealed record BillReturnRow(string ReturnNo, string DateText, string MethodText, string RefundText);
