using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The product variant create/edit dialog content. Markup and nothing else: every behaviour it
/// has is a binding to <see cref="ViewModels.Catalogue.ProductVariantEditViewModel"/>.
/// </summary>
public partial class ProductVariantEditView : UserControl
{
    public ProductVariantEditView()
    {
        InitializeComponent();
    }
}
