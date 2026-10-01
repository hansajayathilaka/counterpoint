using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// RPT-03, the profit report screen (task P3-T05): net sales, COGS, gross profit and margin by
/// period, category, brand or item, with a drill-down from a period or an item to its bills.
/// </summary>
/// <remarks>
/// <b>Owner-only, and not by this class.</b> Nothing here decides who may run it: the first thing
/// <see cref="IProfitReportQuery"/> does for a cashier session is refuse with
/// <see cref="Counterpoint.Application.Security.NotAuthorisedException"/>, which
/// <see cref="ReportViewModelBase"/> shows as a sentence. The shell's nav item is hidden from a
/// cashier as a courtesy; the query is the control (SRS FR-9.4, CLAUDE.md invariant 8, AC-17).
/// </remarks>
public sealed partial class ProfitReportViewModel : ReportViewModelBase
{
    private readonly EnumChoices<ProfitGrouping> _groupingChoices = new(
        (ProfitGrouping.Month, "By month"),
        (ProfitGrouping.Day, "By day"),
        (ProfitGrouping.Category, "By category"),
        (ProfitGrouping.Brand, "By brand"),
        (ProfitGrouping.Item, "By item"));

    private readonly IProfitReportQuery _query;

    [ObservableProperty]
    private string _selectedGroupingLabel;

    [ObservableProperty]
    private string _netSalesText = "-";

    [ObservableProperty]
    private string _cogsText = "-";

    [ObservableProperty]
    private string _grossProfitText = "-";

    [ObservableProperty]
    private string _marginText = "-";

    [ObservableProperty]
    private string _discountsText = "-";

    [ObservableProperty]
    private string _returnsText = "-";

    public ProfitReportViewModel(IProfitReportQuery query, ISalesBillQuery bills, TimeProvider timeProvider)
        : base(timeProvider, new BillDrillDownViewModel(bills))
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
        _selectedGroupingLabel = _groupingChoices.Label(ProfitGrouping.Month);

        DrillCommand = new AsyncRelayCommand<ProfitRowViewModel>(DrillAsync);
    }

    /// <summary>The labels the grouping combo lists.</summary>
    public IReadOnlyList<string> GroupingLabels => _groupingChoices.Labels;

    /// <summary>The grouping currently chosen.</summary>
    public ProfitGrouping Grouping => _groupingChoices.Value(SelectedGroupingLabel);

    public ObservableCollection<ProfitRowViewModel> Rows { get; } = [];

    /// <summary>Lists the bills behind a period or item row.</summary>
    public IAsyncRelayCommand<ProfitRowViewModel> DrillCommand { get; }

    /// <summary>Runs the report for the chosen range and grouping.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await GuardedAsync(
            async range =>
            {
                var grouping = Grouping;
                var report = await _query.GetProfitReportAsync(range, grouping, cancellationToken).ConfigureAwait(true);

                var totals = report.Totals;
                NetSalesText = ReportText.Money(totals.NetSales);
                CogsText = ReportText.Money(totals.Cogs);
                GrossProfitText = ReportText.Money(totals.GrossProfit);
                MarginText = ReportText.Percent(totals.MarginRate);
                DiscountsText = ReportText.Money(totals.Discounts);
                ReturnsText = ReportText.Money(totals.ReturnsValue);

                Rows.Clear();
                foreach (var row in report.Rows)
                {
                    Rows.Add(new ProfitRowViewModel(row, grouping, DrillCommand));
                }

                Status = Rows.Count == 0 ? "No sales in this range." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }

    private async Task DrillAsync(ProfitRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null || !row.CanDrill || LastRange is not { } range)
        {
            return;
        }

        if (row.Period is { } period)
        {
            await Drill.OpenBillListAsync(
                new BillListFilter(period),
                "Bills " + ReportText.Date(period.From) + " to " + ReportText.Date(period.To),
                cancellationToken).ConfigureAwait(true);
            return;
        }

        await Drill.OpenBillListAsync(
            new BillListFilter(range, ProductVariantId: row.Key),
            "Bills containing " + row.Name + ", " + ReportText.Date(range.From) + " to " + ReportText.Date(range.To),
            cancellationToken).ConfigureAwait(true);
    }
}
