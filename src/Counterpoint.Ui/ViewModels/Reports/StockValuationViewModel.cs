using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The stock valuation screen (task P3-T06, SRS RPT-09): every stocked item's quantity at its moving-average cost and
/// at its retail price, with totals, optionally for one category.
/// </summary>
/// <remarks>
/// <para>
/// Owner-only in the Application layer (<see cref="IStockValuationQuery"/>); cost is the point of it.
/// </para>
/// <para>
/// <b>"As at" is now.</b> The screen says so and stamps the moment it ran: it values the current balances at the
/// current costs, and a past date cannot be reconstructed from them. There is no as-at date box.
/// </para>
/// </remarks>
public sealed partial class StockValuationViewModel : ReportScreenViewModelBase
{
    internal const string ReportName = "stock valuation";

    private readonly IStockValuationQuery _query;
    private readonly IReportFilterLookup _filters;
    private readonly TimeProvider _timeProvider;
    private readonly ReportFilterChoices _categories = new();

    [ObservableProperty]
    private string _selectedCategoryLabel = ReportFilterChoices.AllLabel;

    [ObservableProperty]
    private string _asAtText = "As at now - current stock at current costs.";

    [ObservableProperty]
    private string _totalCostText = "-";

    [ObservableProperty]
    private string _totalPriceText = "-";

    public StockValuationViewModel(
        IStockValuationQuery query,
        IReportFilterLookup filters,
        TimeProvider timeProvider,
        ILogger<StockValuationViewModel>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _query = query;
        _filters = filters;
        _timeProvider = timeProvider;

        Lines = new ReportTableViewModel(
            "Stock",
            "Run the report to see the valuation.",
            new ReportColumn("SKU", 130),
            new ReportColumn("Item", 260),
            new ReportColumn("Category", 160),
            new ReportColumn("On hand", 120, IsNumeric: true),
            new ReportColumn("Cost each", 100, IsNumeric: true),
            new ReportColumn("Value at cost", 130, IsNumeric: true),
            new ReportColumn("Price each", 100, IsNumeric: true),
            new ReportColumn("Value at price", 130, IsNumeric: true));
    }

    /// <summary>The labels the category combo lists.</summary>
    public ObservableCollection<string> CategoryLabels => _categories.Labels;

    public ReportTableViewModel Lines { get; }

    /// <summary>Runs the valuation for the chosen category.</summary>
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

                var report = await _query.GetValuationAsync(
                    new StockValuationFilter(_categories.IdFor(SelectedCategoryLabel)), cancellationToken).ConfigureAwait(true);

                AsAtText = "As at now (" + ReportText.Stamp(_timeProvider.GetLocalNow())
                    + ") - current stock at current costs. A past date cannot be valued.";
                TotalCostText = ReportText.Money(report.TotalValue);
                TotalPriceText = ReportText.Money(report.TotalValueAtSellingPrice);

                Lines.Clear();
                foreach (var line in report.Lines)
                {
                    Lines.Add(
                        line.Sku,
                        line.ProductDescription,
                        line.CategoryName,
                        ReportText.Quantity(line.QtyOnHandBase, line.BaseUomSymbol),
                        ReportText.Money(line.CostAvg),
                        ReportText.Money(line.Value),
                        ReportText.Money(line.SellingPrice),
                        ReportText.Money(line.ValueAtSellingPrice));
                }

                Status = report.Lines.Count == 0 ? "No stock on the books for this selection." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }
}
