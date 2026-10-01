using System;
using System.Windows.Input;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>One bill in the drill-down list - a display-ready projection of <see cref="SalesBillRow"/>.</summary>
public sealed class BillRowViewModel
{
    public BillRowViewModel(SalesBillRow row, ICommand openCommand)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(openCommand);

        SaleId = row.SaleId;
        BillNo = row.BillNo;
        SoldAtText = row.SoldAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        CustomerName = row.CustomerName;
        CashierName = row.CashierName;
        GrossText = ReportText.Money(row.Gross);
        DiscountsText = ReportText.Money(row.Discounts);
        TaxText = ReportText.Money(row.Tax);
        NetText = ReportText.Money(row.Net);
        TotalText = ReportText.Money(row.Total);
        ReturnedText = row.ReturnedSubtotal.IsZero ? string.Empty : ReportText.Money(row.ReturnedSubtotal);
        OpenCommand = openCommand;
        OpenParameter = this;
    }

    /// <summary>The bill's row id - what <see cref="ISalesBillQuery.GetBillAsync"/> is asked for.</summary>
    public long SaleId { get; }

    public string BillNo { get; }

    public string SoldAtText { get; }

    public string CustomerName { get; }

    public string CashierName { get; }

    public string GrossText { get; }

    public string DiscountsText { get; }

    public string TaxText { get; }

    public string NetText { get; }

    public string TotalText { get; }

    public string ReturnedText { get; }

    /// <summary>Opens this bill (the drill-down's <c>OpenBillCommand</c>).</summary>
    public ICommand OpenCommand { get; }

    /// <summary>The command parameter for <see cref="OpenCommand"/>: this row.</summary>
    public BillRowViewModel OpenParameter { get; }
}
