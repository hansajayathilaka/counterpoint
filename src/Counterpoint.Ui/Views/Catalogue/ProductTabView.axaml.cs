using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The product tab. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Catalogue.ProductTabViewModel"/>.
/// </summary>
public partial class ProductTabView : UserControl
{
    public ProductTabView()
    {
        InitializeComponent();
    }
}
