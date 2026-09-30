using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// The slow-moving and dead stock screen (task P3-T06, SRS RPT-12): items holding stock with no sale in the last N
/// days, and the value tied up in them.
/// </summary>
/// <remarks>
/// Owner-only in the Application layer (<see cref="ISlowMovingStockQuery"/>): the value tied up is cost. "No sale"
/// means no <c>SALE</c> movement - receipts, counts and adjustments do not restart the clock; an item that has never
/// sold is measured from the day its stock first arrived (docs/report-definitions.md).
/// </remarks>
public sealed partial class SlowMovingStockViewModel : ReportScreenViewModelBase
{
    internal const string ReportName = "slow-moving stock";
    internal const int MaxDays = 3650;

    private readonly ISlowMovingStockQuery _query;
    private readonly IReportFilterLookup _filters;
    private readonly TimeProvider _timeProvider;
    private readonly ReportFilterChoices _categories = new();

    [ObservableProperty]
    private string _daysText = "90";

    [ObservableProperty]
    private string _daysValidation = string.Empty;

    [ObservableProperty]
    private string _selectedCategoryLabel = ReportFilterChoices.AllLabel;

    [ObservableProperty]
    private string _totalValueText = "-";

    [ObservableProperty]
    private string _itemCountText = "-";

    public SlowMovingStockViewModel(
        ISlowMovingStockQuery query,
        IReportFilterLookup filters,
        TimeProvider timeProvider,
        ILogger<SlowMovingStockViewModel>? logger = null)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(filters);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _query = query;
        _filters = filters;
        _timeProvider = timeProvider;

        Lines = new ReportTableViewModel(
            "Stock with no sale in the period",
            "No stock has gone that long without a sale.",
            new ReportColumn("SKU", 130),
            new ReportColumn("Item", 260),
            new ReportColumn("Category", 150),
            new ReportColumn("On hand", 110, IsNumeric: true),
            new ReportColumn("Last sale", 150),
            new ReportColumn("Idle days", 90, IsNumeric: true),
            new ReportColumn("Cost each", 100, IsNumeric: true),
            new ReportColumn("Value tied up", 130, IsNumeric: true));
    }

    public ObservableCollection<string> CategoryLabels => _categories.Labels;

    public ReportTableViewModel Lines { get; }

    /// <summary>Runs the report for the typed number of days and the chosen category.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        DaysValidation = string.Empty;
        if (!int.TryParse(DaysText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days < 1 || days > MaxDays)
        {
            DaysValidation = "Type a whole number of days from 1 to " + MaxDays.ToString(CultureInfo.InvariantCulture) + ".";
            return;
        }

        await RunGuardedAsync(
            ReportName,
            async () =>
            {
                if (!_categories.IsLoaded)
                {
                    _categories.Load(await _filters.ListCategoriesAsync(cancellationToken).ConfigureAwait(true));
                }

                var now = _timeProvider.GetLocalNow();
                var lines = await _query.FindAsync(
                    new SlowMovingFilter(now.AddDays(-days), _categories.IdFor(SelectedCategoryLabel)),
                    cancellationToken).ConfigureAwait(true);

                Lines.Clear();
                var total = Money.Zero;
                foreach (var line in lines)
                {
                    total += line.ValueTiedUp;
                    Lines.Add(
                        line.Sku,
                        line.ProductDescription,
                        line.CategoryName,
                        ReportText.Quantity(line.QtyOnHandBase, line.BaseUomSymbol),
                        line.LastSaleAt is { } sold
                            ? ReportText.Date(DateOnly.FromDateTime(sold.DateTime))
                            : "Never (since " + ReportText.Date(DateOnly.FromDateTime(line.IdleSince.DateTime)) + ")",
                        ReportText.Count((now - line.IdleSince).Days),
                        ReportText.Money(line.CostAvg),
                        ReportText.Money(line.ValueTiedUp));
                }

                ItemCountText = ReportText.Count(lines.Count);
                TotalValueText = ReportText.Money(total);
                Status = lines.Count == 0 ? "No stock has gone that long without a sale." : string.Empty;
            },
            cancellationToken).ConfigureAwait(true);
    }
}
