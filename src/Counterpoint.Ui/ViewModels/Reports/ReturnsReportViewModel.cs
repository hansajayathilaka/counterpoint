using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The returns report screen (task P3-T05, SRS RPT-14): returns by reason, item, disposition,
/// linked versus unlinked and refund method, with the return rate against sales.
/// </summary>
/// <remarks>
/// Owner-only in the Application layer (<see cref="IReturnsReportQuery"/>); a refusal is shown as a
/// sentence, never worked around. No drill-down: a returns group does not name a set of bills.
/// </remarks>
public sealed partial class ReturnsReportViewModel : ReportViewModelBase
{
    private readonly IReturnsReportQuery _query;

    [ObservableProperty]
    private string _returnCountText = "-";

    [ObservableProperty]
    private string _returnsSubtotalText = "-";

    [ObservableProperty]
    private string _totalRefundedText = "-";

    [ObservableProperty]
    private string _valueRateText = "-";

    [ObservableProperty]
    private string _countRateText = "-";

    public ReturnsReportViewModel(IReturnsReportQuery query, ISalesBillQuery bills, TimeProvider timeProvider)
        : base(timeProvider, new BillDrillDownViewModel(bills))
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;
    }

    public ObservableCollection<ReturnsGroupRowViewModel> ByReason { get; } = [];

    public ObservableCollection<ReturnsGroupRowViewModel> ByItem { get; } = [];

    public ObservableCollection<ReturnsGroupRowViewModel> ByDisposition { get; } = [];

    public ObservableCollection<ReturnsGroupRowViewModel> ByLinkage { get; } = [];

    public ObservableCollection<ReturnsGroupRowViewModel> ByRefundMethod { get; } = [];

    /// <summary>Runs the report for the chosen range.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await GuardedAsync(
            async range =>
            {
                var report = await _query.GetReturnsReportAsync(range, cancellationToken).ConfigureAwait(true);

                ReturnCountText = ReportText.Count(report.ReturnCount);
                ReturnsSubtotalText = ReportText.Money(report.ReturnsSubtotal);
                TotalRefundedText = ReportText.Money(report.TotalRefunded);
                ValueRateText = ReportText.Percent(report.ValueReturnRate);
                CountRateText = ReportText.Percent(report.CountReturnRate);

                Fill(ByReason, report.ByReason, showQuantity: true);
                Fill(ByItem, report.ByItem, showQuantity: true);
                Fill(ByDisposition, report.ByDisposition, showQuantity: true);
                Fill(ByLinkage, report.ByLinkage, showQuantity: false);
                Fill(ByRefundMethod, report.ByRefundMethod, showQuantity: false);

                Status = report.ReturnCount == 0 ? "No returns in this range." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }

    private static void Fill(
        ObservableCollection<ReturnsGroupRowViewModel> target,
        System.Collections.Generic.IReadOnlyList<ReturnsGroupRow> rows,
        bool showQuantity)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(new ReturnsGroupRowViewModel(
                row.Key,
                ReportText.Count(row.Count),
                showQuantity ? ReportText.Quantity(row.QtyBase, uomSymbol: null) : string.Empty,
                ReportText.Money(row.Value),
                ReportText.Percent(row.ShareOfValue)));
        }
    }
}

/// <summary>One group of a returns breakdown, display-ready.</summary>
public sealed record ReturnsGroupRowViewModel(string Key, string CountText, string QuantityText, string ValueText, string ShareText);
