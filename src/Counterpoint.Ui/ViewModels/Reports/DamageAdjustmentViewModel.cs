using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The damage and shrinkage screen (task P3-T06, SRS RPT-15): write-offs, damage and adjustments by reason, and
/// damaged returns, with quantity and value at recorded cost.
/// </summary>
/// <remarks>Owner-only in the Application layer (<see cref="IDamageAdjustmentReportQuery"/>); every value is cost.</remarks>
public sealed partial class DamageAdjustmentViewModel : RangedReportScreenViewModelBase
{
    internal const string ReportName = "damage and adjustments";

    private readonly IDamageAdjustmentReportQuery _query;

    [ObservableProperty]
    private string _netValueText = "-";

    [ObservableProperty]
    private string _totalLossText = "-";

    [ObservableProperty]
    private string _totalGainText = "-";

    public DamageAdjustmentViewModel(
        IDamageAdjustmentReportQuery query,
        TimeProvider timeProvider,
        ILogger<DamageAdjustmentViewModel>? logger = null)
        : base(timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;

        Rows = new ReportTableViewModel(
            "By reason",
            "No damage, write-offs or adjustments in this range.",
            new ReportColumn("Kind", 160),
            new ReportColumn("Reason", 340),
            new ReportColumn("Count", 80, IsNumeric: true),
            new ReportColumn("Quantity", 110, IsNumeric: true),
            new ReportColumn("Value at cost", 130, IsNumeric: true));
    }

    public ReportTableViewModel Rows { get; }

    /// <summary>Runs the report for the chosen range.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RunRangedAsync(
            ReportName,
            async range =>
            {
                var report = await _query.GetReportAsync(range, cancellationToken).ConfigureAwait(true);

                NetValueText = ReportText.Money(report.NetValue);
                TotalLossText = ReportText.Money(report.TotalLoss);
                TotalGainText = ReportText.Money(report.TotalGain);

                Rows.Clear();
                foreach (var row in report.Rows)
                {
                    Rows.Add(
                        row.Source switch
                        {
                            DamageSource.Damage => "Damage write-off",
                            DamageSource.DamagedReturn => "Damaged return",
                            _ => "Adjustment",
                        },
                        row.Reason,
                        ReportText.Count(row.Count),
                        ReportText.Decimal(row.QtyBase.Value),
                        ReportText.Money(row.Value));
                }

                Status = report.Rows.Count == 0 ? "No damage, write-offs or adjustments in this range." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }
}
