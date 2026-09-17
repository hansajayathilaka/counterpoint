using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// Task P3-T14: the sales screen's side panel, extracted from <c>SalesWindow.axaml</c> into its
/// own <see cref="UserControl"/> - see <c>SalesSidePanelView.axaml</c>'s own remarks for why. Pure
/// markup; every behaviour (F1-F12, scan-box focus, sale building) stays on
/// <see cref="SalesWindow"/> and <c>SalesViewModel</c>, untouched by this task.
/// </summary>
public partial class SalesSidePanelView : UserControl
{
    public SalesSidePanelView()
    {
        InitializeComponent();
    }
}
