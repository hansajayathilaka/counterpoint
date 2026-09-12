using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Purchasing;

/// <summary>
/// The owner's purchase-order screen. Markup and nothing else: every behaviour it has is a
/// binding to <see cref="ViewModels.Purchasing.PurchaseOrderViewModel"/> (SRS FR-4.5, FR-4.6, FR-4.10).
/// </summary>
public partial class PurchaseOrderWindow : Window
{
    public PurchaseOrderWindow()
    {
        InitializeComponent();
    }
}
