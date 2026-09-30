using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The tax report screen (task P3-T06, SRS RPT-19): taxable value and tax by rate, tax on returns netted off,
/// under a header that names the shop, its registration number, the tax's own name and the pricing basis.
/// </summary>
/// <remarks>
/// Owner-only in the Application layer (<see cref="ITaxReportQuery"/>). No rate, label or regime is written
/// here: every word of the header and every rate comes from the query's result (Q-02).
/// </remarks>
public sealed partial class TaxReportViewModel : RangedReportScreenViewModelBase
{
    internal const string ReportName = "tax";

    private readonly ITaxReportQuery _query;

    [ObservableProperty]
    private string _shopText = "-";

    [ObservableProperty]
    private string _registrationText = "-";

    [ObservableProperty]
    private string _basisText = "-";

    [ObservableProperty]
    private string _totalSalesTaxableText = "-";

    [ObservableProperty]
    private string _totalSalesTaxText = "-";

    [ObservableProperty]
    private string _totalReturnsTaxText = "-";

    [ObservableProperty]
    private string _netTaxText = "-";

    [ObservableProperty]
    private string _reconciliationText = string.Empty;

    [ObservableProperty]
    private bool _reconciliationWarning;

    public TaxReportViewModel(ITaxReportQuery query, TimeProvider timeProvider, ILogger<TaxReportViewModel>? logger = null)
        : base(timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        _query = query;

        Rates = new ReportTableViewModel(
            "Tax by rate",
            "Run the report to see tax by rate.",
            new ReportColumn("Rate", 260),
            new ReportColumn("Sales (taxable value)", 150, IsNumeric: true),
            new ReportColumn("Tax on sales", 120, IsNumeric: true),
            new ReportColumn("Returns (taxable value)", 160, IsNumeric: true),
            new ReportColumn("Tax on returns", 120, IsNumeric: true),
            new ReportColumn("Net taxable value", 150, IsNumeric: true),
            new ReportColumn("Net tax due", 120, IsNumeric: true));
    }

    public ReportTableViewModel Rates { get; }

    /// <summary>Runs the report for the chosen range.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RunRangedAsync(
            ReportName,
            async range =>
            {
                var report = await _query.GetTaxReportAsync(range, cancellationToken).ConfigureAwait(true);
                var taxName = string.IsNullOrWhiteSpace(report.Header.TaxLabel) ? "Tax" : report.Header.TaxLabel;

                ShopText = string.IsNullOrWhiteSpace(report.Header.ShopName) ? "(shop name not set)" : report.Header.ShopName;
                RegistrationText = string.IsNullOrWhiteSpace(report.Header.TaxRegistrationNumber)
                    ? taxName + " registration number: not set"
                    : taxName + " registration number: " + report.Header.TaxRegistrationNumber;
                BasisText = report.Header.PricesIncludeTax
                    ? "Prices include " + taxName + ". Taxable values below exclude it."
                    : "Prices exclude " + taxName + "; it is added on top. Taxable values below are the prices charged.";

                TotalSalesTaxableText = ReportText.Money(report.TotalSalesTaxable);
                TotalSalesTaxText = ReportText.Money(report.TotalSalesTax);
                TotalReturnsTaxText = ReportText.Money(report.TotalReturnsTax);
                NetTaxText = ReportText.Money(report.NetTax);

                Rates.Clear();
                foreach (var row in report.Rows)
                {
                    Rates.Add(
                        RateLabel(row, taxName),
                        ReportText.Money(row.SalesTaxable),
                        ReportText.Money(row.SalesTax),
                        ReportText.Money(row.ReturnsTaxable),
                        ReportText.Money(row.ReturnsTax),
                        ReportText.Money(row.NetTaxable),
                        ReportText.Money(row.NetTax));
                }

                if (report.Rows.Count > 0)
                {
                    Rates.AddBold(
                        "Total",
                        ReportText.Money(report.TotalSalesTaxable),
                        ReportText.Money(report.TotalSalesTax),
                        ReportText.Money(report.TotalReturnsTaxable),
                        ReportText.Money(report.TotalReturnsTax),
                        ReportText.Money(report.TotalNetTaxable),
                        ReportText.Money(report.NetTax));
                }

                ReconciliationWarning = !report.IsReconciled;
                ReconciliationText = report.IsReconciled
                    ? taxName + " on sales ties to the bill lines and bill headers for this period."
                    : taxName + " on sales does not tie to the bill lines ("
                        + ReportText.Money(report.SaleLineTaxTotal) + ") or bill headers ("
                        + ReportText.Money(report.SaleHeaderTaxTotal) + "). Do not file this report; ask for support.";

                Status = report.Rows.Count == 0 ? "No sales or returns in this range." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }

    private static string RateLabel(TaxReportRow row, string taxName)
    {
        if (row.Kind == TaxReportRowKind.UnlinkedReturns || row.Rate is not { } rate)
        {
            return "Rate unknown (unlinked returns)";
        }

        return rate.IsZero
            ? "Exempt / zero-rated (0%)"
            : taxName + " " + ReportText.Rate(rate);
    }
}
