using System;
using System.Collections.Generic;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The four report screens the back office's Reports nav group hosts (task P3-T05), and which one a
/// section name selects. A thin container: each screen owns its own state and its own query.
/// </summary>
public sealed class ReportsViewModel : ViewModelBase
{
    /// <summary>RPT-01, the sales summary.</summary>
    public const string SalesSummarySection = "Sales summary";

    /// <summary>RPT-02, sales by item, category and brand.</summary>
    public const string SalesByItemSection = "Sales by item";

    /// <summary>RPT-03, the profit report (owner-only in the Application layer).</summary>
    public const string ProfitSection = "Profit";

    /// <summary>The returns report (owner-only in the Application layer).</summary>
    public const string ReturnsSection = "Returns";

    public ReportsViewModel(
        SalesSummaryReportViewModel salesSummary,
        SalesByItemReportViewModel salesByItem,
        ProfitReportViewModel profit,
        ReturnsReportViewModel returns)
    {
        ArgumentNullException.ThrowIfNull(salesSummary);
        ArgumentNullException.ThrowIfNull(salesByItem);
        ArgumentNullException.ThrowIfNull(profit);
        ArgumentNullException.ThrowIfNull(returns);

        SalesSummary = salesSummary;
        SalesByItem = salesByItem;
        Profit = profit;
        Returns = returns;
    }

    public SalesSummaryReportViewModel SalesSummary { get; }

    public SalesByItemReportViewModel SalesByItem { get; }

    public ProfitReportViewModel Profit { get; }

    public ReturnsReportViewModel Returns { get; }

    /// <summary>
    /// Runs the screen <paramref name="section"/> names over its current range, so a section opens
    /// with figures rather than an empty table (the same "re-read on every real visit" rule the
    /// Catalogue and Overview sections follow). An unknown name does nothing.
    /// </summary>
    public void Load(string section)
    {
        ArgumentNullException.ThrowIfNull(section);

        switch (section)
        {
            case SalesSummarySection:
                SalesSummary.RunCommand.Execute(null);
                break;
            case SalesByItemSection:
                SalesByItem.RunCommand.Execute(null);
                break;
            case ProfitSection:
                Profit.RunCommand.Execute(null);
                break;
            case ReturnsSection:
                Returns.RunCommand.Execute(null);
                break;
        }
    }

    /// <summary>Returns every screen to its report view, dropping any drill-down left open.</summary>
    public void CloseDrillDowns()
    {
        SalesSummary.Drill.Close();
        SalesByItem.Drill.Close();
        Profit.Drill.Close();
        Returns.Drill.Close();
    }
}
