using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The supplier tab. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Catalogue.SupplierTabViewModel"/>.
/// </summary>
public partial class SupplierTabView : UserControl
{
    public SupplierTabView()
    {
        InitializeComponent();
    }
}
