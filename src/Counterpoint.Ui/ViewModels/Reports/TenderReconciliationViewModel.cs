using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The tender reconciliation screen (task P3-T06; not SRS RPT-05, which is the Z report): tenders by type for the
/// period, the closed shifts' Z tenders against the period's payments, the difference (which must be zero) and any
/// trading that is "not Z'd yet".
/// </summary>
/// <remarks>Owner-only in the Application layer (<see cref="ITenderReconciliationQuery"/>).</remarks>
public sealed partial class TenderReconciliationViewModel : RangedReportScreenViewModelBase
{
    internal const string ReportName = "tender reconciliation";

    private readonly ITenderReconciliationQuery _query;

    [ObservableProperty]
    private string _zNetTotalText = "-";

    [ObservableProperty]
    private string _rangeNetTotalText = "-";

    [ObservableProperty]
    private string _differenceText = "-";

    [ObservableProperty]
    private string _tieOutText = string.Empty;

    [ObservableProperty]
    private bool _tieOutWarning;

    public TenderReconciliationViewModel(
        ITenderReconciliationQuery query,
        TimeProvider timeProvider,
        ILogger<TenderReconciliationViewModel>? logger = null)
        : base(timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;

        ByTender = new ReportTableViewModel(
            "Tie-out by tender type",
            "Run the report to see the tie-out.",
            new ReportColumn("Tender", 170),
            new ReportColumn("Z sales", 110, IsNumeric: true),
            new ReportColumn("Z refunds", 110, IsNumeric: true),
            new ReportColumn("Z net", 110, IsNumeric: true),
            new ReportColumn("Payments sales", 130, IsNumeric: true),
            new ReportColumn("Payments refunds", 140, IsNumeric: true),
            new ReportColumn("Payments net", 120, IsNumeric: true),
            new ReportColumn("Difference", 110, IsNumeric: true));

        Shifts = new ReportTableViewModel(
            "Closed shifts (Z reports) in the range",
            "No shift closed in this range.",
            new ReportColumn("Shift", 150),
            new ReportColumn("Date", 110),
            new ReportColumn("Tender", 170),
            new ReportColumn("Sales", 110, IsNumeric: true),
            new ReportColumn("Refunds", 110, IsNumeric: true),
            new ReportColumn("Net", 110, IsNumeric: true));

        NotZd = new ReportTableViewModel(
            "Not Z'd yet, or dated outside the range",
            "Everything in this range is on a closed shift's Z report, and nothing on those reports falls outside it.",
            new ReportColumn("Shift", 150),
            new ReportColumn("Status", 90),
            new ReportColumn("Date", 110),
            new ReportColumn("Sales", 80, IsNumeric: true),
            new ReportColumn("Returns", 80, IsNumeric: true),
            new ReportColumn("Why", 520));
    }

    public ReportTableViewModel ByTender { get; }

    public ReportTableViewModel Shifts { get; }

    public ReportTableViewModel NotZd { get; }

    /// <summary>Runs the reconciliation for the chosen range.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RunRangedAsync(
            ReportName,
            async range =>
            {
                var report = await _query.GetReconciliationAsync(range, cancellationToken).ConfigureAwait(true);

                ZNetTotalText = ReportText.Money(report.ZNetTotal);
                RangeNetTotalText = ReportText.Money(report.RangeNetTotal);
                DifferenceText = ReportText.Money(report.Difference);

                ByTender.Clear();
                foreach (var row in report.ByTender)
                {
                    var cells = new[]
                    {
                        ReportText.Token(row.TenderType),
                        ReportText.Money(row.ZSales),
                        ReportText.Money(row.ZRefunds),
                        ReportText.Money(row.ZNet),
                        ReportText.Money(row.RangeSales),
                        ReportText.Money(row.RangeRefunds),
                        ReportText.Money(row.RangeNet),
                        ReportText.Money(row.Difference),
                    };

                    if (row.Difference.Amount != 0m)
                    {
                        ByTender.AddWarning([7], cells);
                    }
                    else
                    {
                        ByTender.Add(cells);
                    }
                }

                if (report.ByTender.Count > 0)
                {
                    ByTender.AddBold(
                        "Total",
                        string.Empty,
                        string.Empty,
                        ReportText.Money(report.ZNetTotal),
                        string.Empty,
                        string.Empty,
                        ReportText.Money(report.RangeNetTotal),
                        ReportText.Money(report.Difference));
                }

                Shifts.Clear();
                foreach (var shift in report.Shifts)
                {
                    foreach (var tender in shift.Tenders)
                    {
                        Shifts.Add(
                            shift.ShiftNo,
                            ReportText.Date(shift.BusinessDate),
                            ReportText.Token(tender.TenderType),
                            ReportText.Money(tender.SalesAmount),
                            ReportText.Money(tender.RefundsAmount),
                            ReportText.Money(tender.NetAmount));
                    }
                }

                NotZd.Clear();
                foreach (var item in report.NotZd)
                {
                    NotZd.AddWarning(
                        [0, 5],
                        item.ShiftNo,
                        ReportText.Token(item.Status),
                        ReportText.Date(item.BusinessDate),
                        ReportText.Count(item.IsDateBoundary ? item.SalesOutsideRange : item.SalesInRange),
                        ReportText.Count(item.IsDateBoundary ? item.ReturnsOutsideRange : item.ReturnsInRange),
                        item.Reason);
                }

                TieOutWarning = !report.IsTiedOut;
                TieOutText = TieOutMessage(report);

                Status = report.Shifts.Count == 0 && report.NotZd.Count == 0 && report.ByTender.Count == 0
                    ? "No trading in this range."
                    : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }

    // A difference is only a reason to "ask for support" when the listed shifts do not account for it. A shift
    // that is still open, or a Z report whose trading straddles the range's edge, is a stated, expected cause.
    private static string TieOutMessage(TenderReconciliation report)
    {
        if (report.IsTiedOut)
        {
            return "Ties out: the closed shifts' Z tenders equal the period's payments, tender by tender.";
        }

        if (report.HasUnexplainedDifference)
        {
            return "Does not tie out: a tender differs between the Z reports and the payments. Ask for support before relying on either.";
        }

        if (report.NotZd.Any(item => !item.IsDateBoundary))
        {
            return "Does not tie out yet: some trading in this range is not on a closed shift's Z report (see below).";
        }

        return "Does not tie out, only because of dates: a shift's Z report is dated inside this range but part of its trading is dated "
            + "outside it (see below). Widen the range to take in the whole shift.";
    }
}
