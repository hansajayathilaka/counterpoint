using System;
using System.Windows.Input;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// One ranked row of the cost-free sales breakdown (RPT-02). <see cref="CanDrill"/> is true for an
/// item row with a product behind it - the only kind that names a set of bills.
/// </summary>
public sealed class BreakdownRowViewModel
{
    public BreakdownRowViewModel(SalesBreakdownRow row, SalesBreakdownDimension dimension, ICommand drillCommand)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(drillCommand);

        Key = row.Key;
        RankText = ReportText.Count(row.Rank);
        Code = row.Code;
        Name = row.Name;
        QuantityText = ReportText.Quantity(row.QtyBase, row.UomSymbol);
        NetText = ReportText.Money(row.Net);
        ShareText = ReportText.Percent(row.ShareOfNet);
        CanDrill = dimension == SalesBreakdownDimension.Item && row.Key is not null;
        DrillCommand = drillCommand;
        DrillParameter = this;
    }

    public long? Key { get; }

    public string RankText { get; }

    public string Code { get; }

    public string Name { get; }

    public string QuantityText { get; }

    public string NetText { get; }

    public string ShareText { get; }

    public bool CanDrill { get; }

    public ICommand DrillCommand { get; }

    public BreakdownRowViewModel DrillParameter { get; }
}

/// <summary>
/// One row of the owner-only profit view (RPT-03, and the cost columns of RPT-02). Built only from a
/// <see cref="ProfitRow"/>, which only <see cref="IProfitReportQuery"/> - refused to a cashier session
/// in the Application layer - returns.
/// </summary>
public sealed class ProfitRowViewModel
{
    public ProfitRowViewModel(ProfitRow row, ProfitGrouping grouping, ICommand drillCommand)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(drillCommand);

        Key = row.Key;
        Period = row.Period;
        RankText = row.Rank is { } rank ? ReportText.Count(rank) : string.Empty;
        Code = row.Code;
        Name = row.Name;
        QuantityText = grouping is ProfitGrouping.Day or ProfitGrouping.Month
            ? string.Empty
            : ReportText.Quantity(row.QtyBase, row.UomSymbol);
        NetText = ReportText.Money(row.Net);
        CogsText = ReportText.Money(row.Cogs);
        GrossProfitText = ReportText.Money(row.GrossProfit);
        MarginText = ReportText.Percent(row.MarginRate);
        ShareText = ReportText.Percent(row.ShareOfNet);
        CanDrill = row.Period is not null || (grouping == ProfitGrouping.Item && row.Key is not null);
        DrillCommand = drillCommand;
        DrillParameter = this;
    }

    public long? Key { get; }

    public ReportDateRange? Period { get; }

    public string RankText { get; }

    public string Code { get; }

    public string Name { get; }

    public string QuantityText { get; }

    public string NetText { get; }

    public string CogsText { get; }

    public string GrossProfitText { get; }

    public string MarginText { get; }

    public string ShareText { get; }

    public bool CanDrill { get; }

    public ICommand DrillCommand { get; }

    public ProfitRowViewModel DrillParameter { get; }
}
