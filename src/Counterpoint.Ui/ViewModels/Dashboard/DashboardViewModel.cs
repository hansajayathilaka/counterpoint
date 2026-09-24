using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Inventory;

namespace Counterpoint.Ui.ViewModels.Dashboard;

/// <summary>
/// The back office's Overview landing content (SRS FR-9.7, UI-16, task P3-T20): KPI cards, a
/// reorder-alerts panel and a recent-sales list, hosted under
/// <see cref="BackOfficeShellViewModel"/>'s Overview nav item (task P3-T18's placeholder).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every figure traces to a named query.</b> The four KPI cards are
/// <see cref="IDashboardQueries.GetSummaryAsync"/>'s own <see cref="DashboardSummary.TodaysSales"/>/
/// <see cref="DashboardSummary.BillCount"/>/<see cref="DashboardSummary.LowStockCount"/>/
/// <see cref="DashboardSummary.CashInDrawer"/> - nothing here computes, approximates or hard-codes
/// a figure of its own. The reorder-alerts panel is <see cref="IReorderListQuery.GetReorderListAsync"/>
/// (task P2-T11), unchanged. The recent-sales list is <see cref="IRecentSalesQuery.GetRecentAsync"/>
/// (task P3-T22). No new Application-layer query or business rule is introduced by this class - it
/// is UI-only, exactly as task P3-T20's "Do this" #1 requires.
/// </para>
/// <para>
/// <b>No reorder screen yet.</b> Task P2-T11's own reorder list has no dedicated screen of its own
/// to link out to - this panel shows the raw list only, per task P3-T20's own "Do this" #2 ("do
/// not build a new reorder screen in this task; note the gap rather than filling it").
/// </para>
/// </remarks>
public sealed partial class DashboardViewModel : ViewModelBase
{
    /// <summary>How many recent sales the landing screen shows (task P3-T22's own query is
    /// unbounded in shape; this caller decides how many to ask for).</summary>
    private const int RecentSalesCount = 8;

    private readonly IDashboardQueries _dashboardQueries;
    private readonly IReorderListQuery _reorderListQuery;
    private readonly IRecentSalesQuery _recentSalesQuery;

    public DashboardViewModel(
        IDashboardQueries dashboardQueries,
        IReorderListQuery reorderListQuery,
        IRecentSalesQuery recentSalesQuery)
    {
        ArgumentNullException.ThrowIfNull(dashboardQueries);
        ArgumentNullException.ThrowIfNull(reorderListQuery);
        ArgumentNullException.ThrowIfNull(recentSalesQuery);

        _dashboardQueries = dashboardQueries;
        _reorderListQuery = reorderListQuery;
        _recentSalesQuery = recentSalesQuery;
    }

    // ---- KPI cards (DashboardSummary.TodaysSales/BillCount/LowStockCount/CashInDrawer) --------

    [ObservableProperty]
    private string _todaysSalesText = "-";

    [ObservableProperty]
    private string _billCountText = "-";

    [ObservableProperty]
    private string _lowStockCountText = "-";

    [ObservableProperty]
    private string _cashInDrawerText = "-";

    /// <summary>
    /// <see cref="DashboardSummary.LowStockCount"/> itself, so the view can draw the low-stock
    /// card's danger tint (task P3-T20 "Do this" #5) without parsing <see cref="LowStockCountText"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLowStockAlerts))]
    private int _lowStockCount;

    /// <summary>True once <see cref="LowStockCount"/> is above zero - the low-stock KPI card's own
    /// danger-tint trigger (task P3-T20 "Do this" #5).</summary>
    public bool HasLowStockAlerts => LowStockCount > 0;

    // ---- Reorder alerts (IReorderListQuery.GetReorderListAsync, task P2-T11) -------------------

    /// <summary>Every product at or below its reorder level, most-under-level first - the same
    /// order <see cref="IReorderListQuery"/> already returns.</summary>
    public ObservableCollection<ReorderAlertRow> ReorderAlerts { get; } = [];

    [ObservableProperty]
    private bool _hasReorderAlerts;

    // ---- Recent sales (IRecentSalesQuery.GetRecentAsync, task P3-T22) ---------------------------

    /// <summary>The last <see cref="RecentSalesCount"/> completed bills, most recent first.</summary>
    public ObservableCollection<RecentSaleRow> RecentSales { get; } = [];

    [ObservableProperty]
    private bool _hasRecentSales;

    // ---- Loading ---------------------------------------------------------------------------------

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _status = string.Empty;

    /// <summary>Whether there is a cancellation/error sentence worth showing under the panels.</summary>
    public bool HasStatus => Status.Length > 0;

    /// <summary>
    /// Re-reads all three queries. Called once when the back office window opens on Overview, and
    /// again whenever the owner asks for it - the "refresh the dashboard" quick action (task
    /// P3-T20 "Do this" #4) binds straight to this command.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        var alreadyBusy = Busy;
        Busy = true;
        try
        {
            var summary = await _dashboardQueries.GetSummaryAsync(cancellationToken).ConfigureAwait(true);
            ApplySummary(summary);

            var reorderAlerts = await _reorderListQuery.GetReorderListAsync(cancellationToken).ConfigureAwait(true);
            ApplyReorderAlerts(reorderAlerts);

            var recentSales = await _recentSalesQuery.GetRecentAsync(RecentSalesCount, cancellationToken)
                .ConfigureAwait(true);
            ApplyRecentSales(recentSales);

            Status = string.Empty;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Cancelled.";
        }
        finally
        {
            Busy = alreadyBusy;
        }
    }

    private void ApplySummary(DashboardSummary summary)
    {
        TodaysSalesText = summary.TodaysSales.Amount.ToString("0.00", CultureInfo.InvariantCulture);
        BillCountText = summary.BillCount.ToString(CultureInfo.InvariantCulture);
        LowStockCount = summary.LowStockCount;
        LowStockCountText = summary.LowStockCount.ToString(CultureInfo.InvariantCulture);
        CashInDrawerText = summary.CashInDrawer is { } cash
            ? cash.Amount.ToString("0.00", CultureInfo.InvariantCulture)
            : "No shift open";
    }

    private void ApplyReorderAlerts(IReadOnlyList<ReorderListLine> lines)
    {
        ReorderAlerts.Clear();
        foreach (var line in lines)
        {
            ReorderAlerts.Add(new ReorderAlertRow(
                line.ProductCode,
                line.ProductDescription,
                line.QtyOnHandBase.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + line.BaseUomSymbol,
                line.ReorderLevel.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + line.BaseUomSymbol,
                line.SuggestedQty.Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + line.BaseUomSymbol,
                line.PreferredSupplierName ?? "(none linked)"));
        }

        HasReorderAlerts = ReorderAlerts.Count > 0;
    }

    private void ApplyRecentSales(IReadOnlyList<RecentSale> sales)
    {
        RecentSales.Clear();
        foreach (var sale in sales)
        {
            RecentSales.Add(new RecentSaleRow(
                sale.BillNo,
                sale.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                sale.CustomerName,
                sale.Total.Amount.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        HasRecentSales = RecentSales.Count > 0;
    }
}

/// <summary>
/// One row of the Overview's reorder-alerts panel - a display-ready projection of
/// <see cref="ReorderListLine"/> (<see cref="IReorderListQuery"/>, task P2-T11). Carries no figure
/// <see cref="ReorderListLine"/> itself does not already provide.
/// </summary>
public sealed record ReorderAlertRow(
    string ProductCode,
    string ProductDescription,
    string OnHandText,
    string ReorderLevelText,
    string SuggestedQtyText,
    string SupplierText);

/// <summary>
/// One row of the Overview's recent-sales list - a display-ready projection of
/// <see cref="RecentSale"/> (<see cref="IRecentSalesQuery"/>, task P3-T22). Carries no figure
/// <see cref="RecentSale"/> itself does not already provide.
/// </summary>
public sealed record RecentSaleRow(
    string BillNo,
    string CompletedAtText,
    string CustomerName,
    string TotalText);
