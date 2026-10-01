using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The supplier purchase summary screen (task P3-T06, SRS RPT-16): goods received by supplier and by item with
/// value, and cost-price movement over the range.
/// </summary>
/// <remarks>Owner-only in the Application layer (<see cref="ISupplierPurchaseReportQuery"/>); it is all cost.</remarks>
public sealed partial class SupplierPurchasesViewModel : RangedReportScreenViewModelBase
{
    internal const string ReportName = "supplier purchases";

    private readonly ISupplierPurchaseReportQuery _query;
    private readonly IReportFilterLookup _filters;
    private readonly ReportFilterChoices _suppliers = new();

    [ObservableProperty]
    private string _selectedSupplierLabel = ReportFilterChoices.AllLabel;

    [ObservableProperty]
    private string _totalValueText = "-";

    [ObservableProperty]
    private string _totalTaxText = "-";

    [ObservableProperty]
    private string _totalPurchasesText = "-";

    public SupplierPurchasesViewModel(
        ISupplierPurchaseReportQuery query,
        IReportFilterLookup filters,
        TimeProvider timeProvider,
        ILogger<SupplierPurchasesViewModel>? logger = null)
        : base(timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(filters);

        _query = query;
        _filters = filters;

        BySupplier = new ReportTableViewModel(
            "By supplier",
            "No goods received in this range.",
            new ReportColumn("Supplier", 280),
            new ReportColumn("Receipts", 90, IsNumeric: true),
            new ReportColumn("Value (landed, excl. tax)", 190, IsNumeric: true),
            new ReportColumn("Tax", 110, IsNumeric: true),
            new ReportColumn("Total", 130, IsNumeric: true));

        ByItem = new ReportTableViewModel(
            "By item",
            "No goods received in this range.",
            new ReportColumn("SKU", 130),
            new ReportColumn("Item", 260),
            new ReportColumn("Quantity", 100, IsNumeric: true),
            new ReportColumn("Value (landed, excl. tax)", 190, IsNumeric: true),
            new ReportColumn("Average cost", 120, IsNumeric: true),
            new ReportColumn("First cost", 110, IsNumeric: true),
            new ReportColumn("Last cost", 110, IsNumeric: true),
            new ReportColumn("Change", 100, IsNumeric: true),
            new ReportColumn("Change %", 90, IsNumeric: true));

        CostMovement = new ReportTableViewModel(
            "Cost-price movement (items whose cost changed between receipts)",
            "No item's cost changed between receipts in this range.",
            new ReportColumn("SKU", 130),
            new ReportColumn("Received", 140),
            new ReportColumn("Supplier", 240),
            new ReportColumn("Receipt", 150),
            new ReportColumn("Cost per base unit", 160, IsNumeric: true));
    }

    /// <summary>The labels the supplier combo lists.</summary>
    public ObservableCollection<string> SupplierLabels => _suppliers.Labels;

    public ReportTableViewModel BySupplier { get; }

    public ReportTableViewModel ByItem { get; }

    public ReportTableViewModel CostMovement { get; }

    /// <summary>Runs the report for the chosen range and supplier.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RunRangedAsync(
            ReportName,
            async range =>
            {
                if (!_suppliers.IsLoaded)
                {
                    _suppliers.Load(await _filters.ListSuppliersAsync(cancellationToken).ConfigureAwait(true));
                }

                var report = await _query.GetReportAsync(range, _suppliers.IdFor(SelectedSupplierLabel), cancellationToken)
                    .ConfigureAwait(true);

                TotalValueText = ReportText.Money(report.TotalValue);
                TotalTaxText = ReportText.Money(report.TotalTax);
                TotalPurchasesText = ReportText.Money(report.TotalPurchases);

                BySupplier.Clear();
                foreach (var row in report.BySupplier)
                {
                    BySupplier.Add(
                        row.SupplierName,
                        ReportText.Count(row.ReceiptCount),
                        ReportText.Money(row.Value),
                        ReportText.Money(row.Tax),
                        ReportText.Money(row.Total));
                }

                ByItem.Clear();
                foreach (var row in report.ByItem)
                {
                    ByItem.Add(
                        row.Sku,
                        row.Description,
                        ReportText.Decimal(row.QtyBase.Value),
                        ReportText.Money(row.Value),
                        ReportText.Money(row.AverageUnitCost),
                        ReportText.Money(row.FirstUnitCost),
                        ReportText.Money(row.LastUnitCost),
                        ReportText.Money(row.CostChange),
                        ReportText.Percent(row.CostChangeRate));
                }

                CostMovement.Clear();
                foreach (var point in report.CostMovement)
                {
                    CostMovement.Add(
                        point.Sku,
                        ReportText.Stamp(point.ReceivedAt),
                        point.SupplierName,
                        point.GrnNo,
                        ReportText.Money(point.UnitCostBase));
                }

                Status = report.BySupplier.Count == 0 ? "No goods received in this range." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }
}
