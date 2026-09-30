using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The shift and cash-variance history screen (task P3-T06, SRS RPT-21, FR-8.6): over and short per closed shift,
/// the note threshold in force, and the direction variance is heading.
/// </summary>
/// <remarks>Owner-only in the Application layer (<see cref="IShiftVarianceHistoryQuery"/>).</remarks>
public sealed partial class ShiftVarianceViewModel : RangedReportScreenViewModelBase
{
    internal const string ReportName = "shift and variance history";

    private readonly IShiftVarianceHistoryQuery _query;

    [ObservableProperty]
    private string _shiftCountText = "-";

    [ObservableProperty]
    private string _netVarianceText = "-";

    [ObservableProperty]
    private string _totalOverText = "-";

    [ObservableProperty]
    private string _totalShortText = "-";

    [ObservableProperty]
    private string _meanAbsoluteText = "-";

    [ObservableProperty]
    private string _thresholdText = "-";

    [ObservableProperty]
    private string _trendText = string.Empty;

    public ShiftVarianceViewModel(
        IShiftVarianceHistoryQuery query,
        TimeProvider timeProvider,
        ILogger<ShiftVarianceViewModel>? logger = null)
        : base(timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;

        Shifts = new ReportTableViewModel(
            "Closed shifts",
            "No shift closed in this range.",
            new ReportColumn("Shift", 140),
            new ReportColumn("Date", 100),
            new ReportColumn("Opened by", 130),
            new ReportColumn("Closed by", 130),
            new ReportColumn("Counted", 110, IsNumeric: true),
            new ReportColumn("Expected", 110, IsNumeric: true),
            new ReportColumn("Variance", 100, IsNumeric: true),
            new ReportColumn("Running total", 120, IsNumeric: true),
            new ReportColumn("Note", 340));
    }

    public ReportTableViewModel Shifts { get; }

    /// <summary>Runs the report for the chosen range.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RunRangedAsync(
            ReportName,
            async range =>
            {
                var history = await _query.GetHistoryAsync(range, cancellationToken).ConfigureAwait(true);

                ShiftCountText = ReportText.Count(history.Rows.Count);
                NetVarianceText = ReportText.Money(history.NetVariance);
                TotalOverText = ReportText.Money(history.TotalOver);
                TotalShortText = ReportText.Money(history.TotalShort);
                MeanAbsoluteText = ReportText.Money(history.MeanAbsoluteVariance);
                ThresholdText = ReportText.Money(history.NoteThreshold) + " - "
                    + ReportText.Count(history.ShiftsOverThreshold) + " shift(s) above it";
                TrendText = history.Trend switch
                {
                    VarianceTrend.Improving => "Improving: the average swing fell from "
                        + ReportText.Money(history.EarlierHalfMeanAbsolute) + " in the earlier shifts to "
                        + ReportText.Money(history.LaterHalfMeanAbsolute) + " in the later ones.",
                    VarianceTrend.Worsening => "Worsening: the average swing rose from "
                        + ReportText.Money(history.EarlierHalfMeanAbsolute) + " in the earlier shifts to "
                        + ReportText.Money(history.LaterHalfMeanAbsolute) + " in the later ones.",
                    VarianceTrend.Steady => "Steady: the average swing is the same in the earlier and later shifts ("
                        + ReportText.Money(history.EarlierHalfMeanAbsolute) + ").",
                    _ => "Not enough closed shifts in this range (at least four) to show a trend.",
                };

                Shifts.Clear();
                foreach (var row in history.Rows)
                {
                    var cells = new[]
                    {
                        row.ShiftNo,
                        ReportText.Date(row.BusinessDate),
                        row.OpenedBy,
                        row.ClosedBy,
                        ReportText.Money(row.CountedCash),
                        ReportText.Money(row.ExpectedCash),
                        ReportText.Money(row.Variance),
                        ReportText.Money(row.CumulativeVariance),
                        row.Note ?? string.Empty,
                    };

                    if (row.ExceedsNoteThreshold)
                    {
                        Shifts.AddWarning([6], cells);
                    }
                    else
                    {
                        Shifts.Add(cells);
                    }
                }

                Status = history.Rows.Count == 0 ? "No shift closed in this range." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }
}
