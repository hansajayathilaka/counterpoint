using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The fast-moving items screen (task P3-T06, SRS RPT-13): top sellers by units and by value over a range.
/// </summary>
/// <remarks>
/// Owner-only in the Application layer (<see cref="IFastMovingReportQuery"/>), a thin projection over the
/// sales-by-item ranking: net of returns, excluding tax, no cost.
/// </remarks>
public sealed partial class FastMovingViewModel : RangedReportScreenViewModelBase
{
    internal const string ReportName = "fast-moving items";
    internal const int MaxTopN = 500;

    private readonly IFastMovingReportQuery _query;

    [ObservableProperty]
    private string _topNText = "20";

    [ObservableProperty]
    private string _topNValidation = string.Empty;

    public FastMovingViewModel(IFastMovingReportQuery query, TimeProvider timeProvider, ILogger<FastMovingViewModel>? logger = null)
        : base(timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;

        ByUnits = new ReportTableViewModel(
            "Top sellers by units",
            "No sales in this range.",
            new ReportColumn("#", 50, IsNumeric: true),
            new ReportColumn("SKU", 130),
            new ReportColumn("Item", 300),
            new ReportColumn("Net units", 120, IsNumeric: true),
            new ReportColumn("Net sales", 130, IsNumeric: true));

        ByValue = new ReportTableViewModel(
            "Top sellers by value",
            "No sales in this range.",
            new ReportColumn("#", 50, IsNumeric: true),
            new ReportColumn("SKU", 130),
            new ReportColumn("Item", 300),
            new ReportColumn("Net sales", 130, IsNumeric: true),
            new ReportColumn("Net units", 120, IsNumeric: true));
    }

    public ReportTableViewModel ByUnits { get; }

    public ReportTableViewModel ByValue { get; }

    /// <summary>Runs the report for the chosen range and top-N.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        TopNValidation = string.Empty;
        if (!int.TryParse(TopNText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var topN) || topN < 1 || topN > MaxTopN)
        {
            TopNValidation = "Type a whole number from 1 to " + MaxTopN.ToString(CultureInfo.InvariantCulture) + ".";
            return;
        }

        await RunRangedAsync(
            ReportName,
            async range =>
            {
                var report = await _query.GetReportAsync(range, topN, cancellationToken).ConfigureAwait(true);

                ByUnits.Clear();
                foreach (var row in report.ByUnits)
                {
                    ByUnits.Add(
                        ReportText.Count(row.Rank),
                        row.Sku,
                        row.Name,
                        ReportText.Quantity(row.QtyBase, row.UomSymbol),
                        ReportText.Money(row.Net));
                }

                ByValue.Clear();
                foreach (var row in report.ByValue)
                {
                    ByValue.Add(
                        ReportText.Count(row.Rank),
                        row.Sku,
                        row.Name,
                        ReportText.Money(row.Net),
                        ReportText.Quantity(row.QtyBase, row.UomSymbol));
                }

                Status = report.ByUnits.Count == 0 && report.ByValue.Count == 0 ? "No sales in this range." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }
}
