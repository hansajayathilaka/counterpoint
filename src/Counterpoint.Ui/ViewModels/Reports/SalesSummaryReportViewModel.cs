using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// RPT-01, the sales summary screen (task P3-T05): the canonical totals, then the by-day, by-hour
/// and by-tender tables, with a drill-down from a day, an hour, or the whole range to its bills.
/// </summary>
/// <remarks>
/// A pure projection of <see cref="ISalesSummaryReportQuery"/>: every figure arrives computed, and
/// nothing on this screen is cost or margin (SRS FR-9.4, CLAUDE.md invariant 8).
/// </remarks>
public sealed partial class SalesSummaryReportViewModel : ReportViewModelBase
{
    private readonly ISalesSummaryReportQuery _query;

    [ObservableProperty]
    private string _billCountText = "-";

    [ObservableProperty]
    private string _averageBillText = "-";

    [ObservableProperty]
    private string _grossText = "-";

    [ObservableProperty]
    private string _discountsText = "-";

    [ObservableProperty]
    private string _taxText = "-";

    [ObservableProperty]
    private string _netText = "-";

    [ObservableProperty]
    private string _returnsText = "-";

    [ObservableProperty]
    private string _tenderTotalText = "-";

    public SalesSummaryReportViewModel(ISalesSummaryReportQuery query, ISalesBillQuery bills, TimeProvider timeProvider)
        : base(timeProvider, new BillDrillDownViewModel(bills))
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;

        DrillDayCommand = new AsyncRelayCommand<SalesDayRowViewModel>(DrillDayAsync);
        DrillHourCommand = new AsyncRelayCommand<SalesHourRowViewModel>(DrillHourAsync);
    }

    /// <summary>Lists one day's bills.</summary>
    public IAsyncRelayCommand<SalesDayRowViewModel> DrillDayCommand { get; }

    /// <summary>Lists one hour of day's bills across the range.</summary>
    public IAsyncRelayCommand<SalesHourRowViewModel> DrillHourCommand { get; }

    public ObservableCollection<SalesDayRowViewModel> ByDay { get; } = [];

    public ObservableCollection<SalesHourRowViewModel> ByHour { get; } = [];

    public ObservableCollection<SalesTenderRowViewModel> ByTender { get; } = [];

    /// <summary>Runs the report for the chosen range.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await GuardedAsync(
            async range =>
            {
                var report = await _query.GetSummaryAsync(range, cancellationToken).ConfigureAwait(true);
                Apply(report);
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Lists every bill in the range (the "by bill" view).</summary>
    [RelayCommand]
    public async Task ShowAllBillsAsync(CancellationToken cancellationToken)
    {
        if (LastRange is not { } range)
        {
            return;
        }

        await Drill.OpenBillListAsync(
            new BillListFilter(range),
            "Bills " + ReportText.Date(range.From) + " to " + ReportText.Date(range.To),
            cancellationToken).ConfigureAwait(true);
    }

    private async Task DrillDayAsync(SalesDayRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null)
        {
            return;
        }

        await Drill.OpenBillListAsync(
            new BillListFilter(ReportDateRange.Custom(row.Date, row.Date)),
            "Bills on " + row.DateText,
            cancellationToken).ConfigureAwait(true);
    }

    private async Task DrillHourAsync(SalesHourRowViewModel? row, CancellationToken cancellationToken)
    {
        if (row is null || LastRange is not { } range)
        {
            return;
        }

        await Drill.OpenBillListAsync(
            new BillListFilter(range, Hour: row.Hour),
            "Bills sold " + row.HourText + ", " + ReportText.Date(range.From) + " to " + ReportText.Date(range.To),
            cancellationToken).ConfigureAwait(true);
    }

    private void Apply(SalesSummaryReport report)
    {
        var totals = report.Totals;
        BillCountText = totals.BillCount.ToString(CultureInfo.InvariantCulture);
        AverageBillText = ReportText.Money(report.AverageBillValue);
        GrossText = ReportText.Money(totals.GrossSales);
        DiscountsText = ReportText.Money(totals.Discounts);
        TaxText = ReportText.Money(totals.Tax);
        NetText = ReportText.Money(totals.NetSales);
        ReturnsText = ReportText.Money(totals.ReturnsValue);
        TenderTotalText = ReportText.Money(totals.TenderTotal);

        ByDay.Clear();
        foreach (var day in report.ByDay)
        {
            ByDay.Add(new SalesDayRowViewModel(day, DrillDayCommand));
        }

        ByHour.Clear();
        foreach (var hour in report.ByHour)
        {
            ByHour.Add(new SalesHourRowViewModel(hour, DrillHourCommand));
        }

        ByTender.Clear();
        foreach (var tender in report.ByTender)
        {
            ByTender.Add(new SalesTenderRowViewModel(
                tender.TenderType,
                ReportText.Money(tender.SalesAmount),
                ReportText.Money(tender.RefundsAmount),
                ReportText.Money(tender.NetAmount)));
        }

        Status = ByDay.Count == 0 ? "No sales or returns in this range." : string.Empty;
    }
}
