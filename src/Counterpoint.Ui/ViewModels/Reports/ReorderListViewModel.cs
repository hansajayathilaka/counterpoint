using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The low-stock and reorder screen (task P3-T06, SRS RPT-10): products at or below their reorder level, grouped by
/// the supplier to buy from, optionally for one supplier or category.
/// </summary>
/// <remarks>
/// Open to both roles (SRS section 9): <see cref="IReorderListQuery"/> carries no cost or margin field, so there is
/// nothing to withhold from a cashier session.
/// </remarks>
public sealed partial class ReorderListViewModel : ReportScreenViewModelBase
{
    internal const string ReportName = "reorder list";

    private readonly IReorderListQuery _query;
    private readonly IReportFilterLookup _filters;
    private readonly ReportFilterChoices _suppliers = new();
    private readonly ReportFilterChoices _categories = new();

    [ObservableProperty]
    private string _selectedSupplierLabel = ReportFilterChoices.AllLabel;

    [ObservableProperty]
    private string _selectedCategoryLabel = ReportFilterChoices.AllLabel;

    [ObservableProperty]
    private string _productCountText = "-";

    public ReorderListViewModel(
        IReorderListQuery query,
        IReportFilterLookup filters,
        ILogger<ReorderListViewModel>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(filters);

        _query = query;
        _filters = filters;

        Lines = new ReportTableViewModel(
            "Products to reorder, by supplier",
            "Nothing is at or below its reorder level for this selection.",
            new ReportColumn("Code", 130),
            new ReportColumn("Product", 280),
            new ReportColumn("Category", 160),
            new ReportColumn("On hand", 110, IsNumeric: true),
            new ReportColumn("Reorder level", 120, IsNumeric: true),
            new ReportColumn("Suggested order", 140, IsNumeric: true));
    }

    public ObservableCollection<string> SupplierLabels => _suppliers.Labels;

    public ObservableCollection<string> CategoryLabels => _categories.Labels;

    public ReportTableViewModel Lines { get; }

    /// <summary>Runs the list for the chosen supplier and category.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await RunGuardedAsync(
            ReportName,
            async () =>
            {
                if (!_suppliers.IsLoaded)
                {
                    _suppliers.Load(await _filters.ListSuppliersAsync(cancellationToken).ConfigureAwait(true));
                }

                if (!_categories.IsLoaded)
                {
                    _categories.Load(await _filters.ListCategoriesAsync(cancellationToken).ConfigureAwait(true));
                }

                var groups = await _query.GetReorderListBySupplierAsync(
                    new ReorderListFilter(_suppliers.IdFor(SelectedSupplierLabel), _categories.IdFor(SelectedCategoryLabel)),
                    cancellationToken).ConfigureAwait(true);

                Lines.Clear();
                var products = 0;
                foreach (var group in groups)
                {
                    var name = group.SupplierId is null ? "No supplier linked" : group.SupplierName;
                    Lines.AddBold(
                        name + " (" + group.Lines.Count.ToString(CultureInfo.InvariantCulture) + ")",
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty,
                        string.Empty);

                    foreach (var line in group.Lines)
                    {
                        products++;
                        Lines.Add(
                            line.ProductCode,
                            line.ProductDescription,
                            line.CategoryName,
                            ReportText.Quantity(line.QtyOnHandBase, line.BaseUomSymbol),
                            ReportText.Quantity(line.ReorderLevel, line.BaseUomSymbol),
                            ReportText.Quantity(line.SuggestedQty, line.BaseUomSymbol));
                    }
                }

                ProductCountText = ReportText.Count(products);
                Status = products == 0 ? "Nothing is at or below its reorder level for this selection." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }
}
