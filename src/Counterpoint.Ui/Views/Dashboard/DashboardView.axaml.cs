using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Dashboard;

/// <summary>
/// The back office's Overview landing content (task P3-T20, SRS FR-9.7, UI-16): KPI cards, a
/// reorder-alerts panel and a recent-sales list. Nothing but markup - every figure is a binding
/// against <c>DashboardViewModel</c>, set as this control's own <c>DataContext</c> by
/// <c>BackOfficeShellWindow.axaml</c> (<c>{Binding Dashboard}</c>).
/// </summary>
public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }
}
