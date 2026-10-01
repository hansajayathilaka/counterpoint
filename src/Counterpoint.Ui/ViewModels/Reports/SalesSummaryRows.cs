using System;
using System.Windows.Input;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>One business date of the sales summary. <see cref="DrillCommand"/> lists that day's bills.</summary>
public sealed class SalesDayRowViewModel
{
    public SalesDayRowViewModel(SalesDayRow row, ICommand drillCommand)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(drillCommand);

        Date = row.BusinessDate;
        DateText = ReportText.Date(row.BusinessDate);
        BillCountText = ReportText.Count(row.BillCount);
        GrossText = ReportText.Money(row.Gross);
        DiscountsText = ReportText.Money(row.Discounts);
        TaxText = ReportText.Money(row.Tax);
        ReturnsText = ReportText.Money(row.ReturnsValue);
        NetText = ReportText.Money(row.Net);
        AverageText = ReportText.Money(row.AverageBillValue);
        DrillCommand = drillCommand;
        DrillParameter = this;
    }

    public DateOnly Date { get; }

    public string DateText { get; }

    public string BillCountText { get; }

    public string GrossText { get; }

    public string DiscountsText { get; }

    public string TaxText { get; }

    public string ReturnsText { get; }

    public string NetText { get; }

    public string AverageText { get; }

    public ICommand DrillCommand { get; }

    public SalesDayRowViewModel DrillParameter { get; }
}

/// <summary>One hour of day of the sales summary. <see cref="DrillCommand"/> lists that hour's bills.</summary>
public sealed class SalesHourRowViewModel
{
    public SalesHourRowViewModel(SalesHourRow row, ICommand drillCommand)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(drillCommand);

        Hour = row.Hour;
        HourText = ReportText.Hour(row.Hour);
        BillCountText = ReportText.Count(row.BillCount);
        GrossText = ReportText.Money(row.Gross);
        DiscountsText = ReportText.Money(row.Discounts);
        TaxText = ReportText.Money(row.Tax);
        NetText = ReportText.Money(row.Net);
        AverageText = ReportText.Money(row.AverageBillValue);
        DrillCommand = drillCommand;
        DrillParameter = this;
    }

    public int Hour { get; }

    public string HourText { get; }

    public string BillCountText { get; }

    public string GrossText { get; }

    public string DiscountsText { get; }

    public string TaxText { get; }

    public string NetText { get; }

    public string AverageText { get; }

    public ICommand DrillCommand { get; }

    public SalesHourRowViewModel DrillParameter { get; }
}

/// <summary>One tender type of the sales summary.</summary>
public sealed record SalesTenderRowViewModel(string TenderType, string SalesText, string RefundsText, string NetText);
