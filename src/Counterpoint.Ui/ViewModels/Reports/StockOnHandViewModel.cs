using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The stock-on-hand screen (task P3-T06, SRS RPT-08): quantity per item in its base and alternate units, with
/// location and reorder level, filtered by category, brand, supplier or location.
/// </summary>
/// <remarks>
/// Open to both roles (SRS section 9), and cost-free at the projection: <see cref="IStockOnHandQuery"/>'s rows have
/// no cost field, so this screen shows a cashier session exactly what it shows the owner.
/// </remarks>
public sealed partial class StockOnHandViewModel : ReportScreenViewModelBase
{
    internal const string ReportName = "stock on hand";

    private readonly IStockOnHandQuery _query;
    private readonly IReportFilterLookup _filters;
    private readonly ReportFilterChoices _categories = new();
    private readonly ReportFilterChoices _brands = new();
    private readonly ReportFilterChoices _suppliers = new();

    [ObservableProperty]
    private string _selectedCategoryLabel = ReportFilterChoices.AllLabel;

    [ObservableProperty]
    private string _selectedBrandLabel = ReportFilterChoices.AllLabel;

    [ObservableProperty]
    private string _selectedSupplierLabel = ReportFilterChoices.AllLabel;

    [ObservableProperty]
    private string _locationText = string.Empty;

    [ObservableProperty]
    private bool _inStockOnly;

    [ObservableProperty]
    private string _itemCountText = "-";

    public StockOnHandViewModel(
        IStockOnHandQuery query,
        IReportFilterLookup filters,
        ILogger<StockOnHandViewModel>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(filters);

        _query = query;
        _filters = filters;

        Lines = new ReportTableViewModel(
            "Stock on hand",
            "No items match.",
            new ReportColumn("SKU", 130),
            new ReportColumn("Item", 260),
            new ReportColumn("Category", 140),
            new ReportColumn("Brand", 120),
            new ReportColumn("Location", 100),
            new ReportColumn("On hand", 110, IsNumeric: true),
            new ReportColumn("Also as", 220),
            new ReportColumn("Reorder level", 110, IsNumeric: true));
    }

    public ObservableCollection<string> CategoryLabels => _categories.Labels;

    public ObservableCollection<string> BrandLabels => _brands.Labels;

    public ObservableCollection<string> SupplierLabels => _suppliers.Labels;

    public ReportTableViewModel Lines { get; }

    /// <summary>Runs the list for the chosen filters.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RunGuardedAsync(
            ReportName,
            async () =>
            {
                if (!_categories.IsLoaded)
                {
                    _categories.Load(await _filters.ListCategoriesAsync(cancellationToken).ConfigureAwait(true));
                }

                if (!_brands.IsLoaded)
                {
                    _brands.Load(await _filters.ListBrandsAsync(cancellationToken).ConfigureAwait(true));
                }

                if (!_suppliers.IsLoaded)
                {
                    _suppliers.Load(await _filters.ListSuppliersAsync(cancellationToken).ConfigureAwait(true));
                }

                var report = await _query.GetStockOnHandAsync(
                    new StockOnHandFilter(
                        _categories.IdFor(SelectedCategoryLabel),
                        _brands.IdFor(SelectedBrandLabel),
                        _suppliers.IdFor(SelectedSupplierLabel),
                        LocationText,
                        InStockOnly),
                    cancellationToken).ConfigureAwait(true);

                Lines.Clear();
                foreach (var line in report.Lines)
                {
                    Lines.Add(
                        line.Sku,
                        line.Description,
                        line.CategoryName,
                        line.BrandName,
                        line.Location,
                        ReportText.Quantity(line.QtyOnHandBase, line.BaseUomSymbol),
                        string.Join(
                            "; ",
                            line.AlternateUnits.Select(unit => ReportText.Decimal(unit.Quantity) + " " + unit.Symbol)),
                        line.ReorderLevel.IsZero ? string.Empty : ReportText.Decimal(line.ReorderLevel.Value));
                }

                ItemCountText = ReportText.Count(report.Lines.Count);
                Status = report.Lines.Count == 0 ? "No items match." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }
}
