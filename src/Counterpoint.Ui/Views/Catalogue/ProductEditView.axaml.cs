using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The product master create/edit dialog content. Markup and nothing else: every behaviour it has
/// is a binding to <see cref="ViewModels.Catalogue.ProductEditViewModel"/>.
/// </summary>
public partial class ProductEditView : UserControl
{
    public ProductEditView()
    {
        InitializeComponent();
    }
}
