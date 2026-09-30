using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Ui.ViewModels.Reports;

/// <summary>
/// RPT-02, sales by item, category or brand, ranked (task P3-T05): quantity, net sales and share for
/// everyone, plus COGS, gross profit and margin when the owner switches them on.
/// </summary>
/// <remarks>
/// <para>
/// <b>Cost columns come from a different query, not a hidden field.</b> The default rows are
/// <see cref="ISalesBreakdownQuery"/>, whose rows have no cost field at all. The owner's margin view
/// re-reads the same ranking from <see cref="IProfitReportQuery"/>, which the Application layer
/// refuses to a cashier session. <see cref="CanShowMargin"/> only decides whether the switch is
/// offered - a courtesy, exactly like the nav rail's other <c>Can*</c> flags; the check that matters
/// is the query's own (SRS FR-9.4, CLAUDE.md invariant 8, AC-17).
/// </para>
/// </remarks>
public sealed partial class SalesByItemReportViewModel : ReportViewModelBase
{
    private readonly EnumChoices<SalesBreakdownDimension> _dimensionChoices = new(
        (SalesBreakdownDimension.Item, "By item"),
        (SalesBreakdownDimension.Category, "By category"),
        (SalesBreakdownDimension.Brand, "By brand"));

    private readonly ISalesBreakdownQuery _breakdown;
    private readonly IProfitReportQuery _profit;
    private readonly ISession _session;

    [ObservableProperty]
    private string _selectedDimensionLabel;

    [ObservableProperty]
    private bool _showMargin;

    [ObservableProperty]
    private string _totalNetText = "-";

    public SalesByItemReportViewModel(
        ISalesBreakdownQuery breakdown,
        IProfitReportQuery profit,
        ISalesBillQuery bills,
        ISession session,
        TimeProvider timeProvider)
        : base(timeProvider, new BillDrillDownViewModel(bills))
    {
        ArgumentNullException.ThrowIfNull(breakdown);
        ArgumentNullException.ThrowIfNull(profit);
        ArgumentNullException.ThrowIfNull(session);

        _breakdown = breakdown;
        _profit = profit;
        _session = session;
        _selectedDimensionLabel = _dimensionChoices.Label(SalesBreakdownDimension.Item);

        DrillCommand = new AsyncRelayCommand<object>(DrillAsync);
    }

    /// <summary>The labels the dimension combo lists.</summary>
    public System.Collections.Generic.IReadOnlyList<string> DimensionLabels => _dimensionChoices.Labels;

    /// <summary>The dimension currently chosen.</summary>
    public SalesBreakdownDimension Dimension => _dimensionChoices.Value(SelectedDimensionLabel);

    /// <summary>
    /// Whether the cost-and-margin switch is offered. A courtesy only: the owner-only query refuses a
    /// cashier session regardless of what this says.
    /// </summary>
    public bool CanShowMargin => _session.Role == Role.Owner;

    /// <summary>True while the cost-free rows show.</summary>
    public bool ShowPlainRows => !ShowMargin;

    /// <summary>The cost-free ranking (<see cref="ISalesBreakdownQuery"/>).</summary>
    public ObservableCollection<BreakdownRowViewModel> Rows { get; } = [];

    /// <summary>The owner's ranking with COGS, gross profit and margin (<see cref="IProfitReportQuery"/>).</summary>
    public ObservableCollection<ProfitRowViewModel> MarginRows { get; } = [];

    /// <summary>Lists the bills behind an item row.</summary>
    public IAsyncRelayCommand<object> DrillCommand { get; }

    partial void OnShowMarginChanged(bool value) => OnPropertyChanged(nameof(ShowPlainRows));

    /// <summary>Runs the report for the chosen range and dimension.</summary>
    [RelayCommand]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await GuardedAsync(
            async range =>
            {
                var dimension = Dimension;

                if (ShowMargin)
                {
                    var report = await _profit.GetProfitReportAsync(range, ToGrouping(dimension), cancellationToken)
                        .ConfigureAwait(true);

                    Rows.Clear();
                    MarginRows.Clear();
                    foreach (var row in report.Rows)
                    {
                        MarginRows.Add(new ProfitRowViewModel(row, ToGrouping(dimension), DrillCommand));
                    }

                    TotalNetText = ReportText.Money(report.Totals.NetSales);
                    Status = MarginRows.Count == 0 ? "No sales in this range." : string.Empty;
                }
                else
                {
                    var report = await _breakdown.GetBreakdownAsync(range, dimension, cancellationToken)
                        .ConfigureAwait(true);

                    MarginRows.Clear();
                    Rows.Clear();
                    foreach (var row in report.Rows)
                    {
                        Rows.Add(new BreakdownRowViewModel(row, dimension, DrillCommand));
                    }

                    TotalNetText = ReportText.Money(report.TotalNet);
                    Status = Rows.Count == 0 ? "No sales in this range." : string.Empty;
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    private async Task DrillAsync(object? row, CancellationToken cancellationToken)
    {
        if (LastRange is not { } range)
        {
            return;
        }

        (long? key, string name) = row switch
        {
            BreakdownRowViewModel plain when plain.CanDrill => (plain.Key, plain.Name),
            ProfitRowViewModel margin when margin.CanDrill => (margin.Key, margin.Name),
            _ => (null, string.Empty),
        };

        if (key is null)
        {
            return;
        }

        await Drill.OpenBillListAsync(
            new BillListFilter(range, ProductVariantId: key),
            "Bills containing " + name + ", " + ReportText.Date(range.From) + " to " + ReportText.Date(range.To),
            cancellationToken).ConfigureAwait(true);
    }

    private static ProfitGrouping ToGrouping(SalesBreakdownDimension dimension) => dimension switch
    {
        SalesBreakdownDimension.Item => ProfitGrouping.Item,
        SalesBreakdownDimension.Category => ProfitGrouping.Category,
        SalesBreakdownDimension.Brand => ProfitGrouping.Brand,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Unknown breakdown dimension."),
    };
}
