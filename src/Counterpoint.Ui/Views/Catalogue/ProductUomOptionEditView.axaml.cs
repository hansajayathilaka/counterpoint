using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The product unit-of-measure option create/edit dialog content. Markup and nothing else: every
/// behaviour it has is a binding to
/// <see cref="ViewModels.Catalogue.ProductUomOptionEditViewModel"/>.
/// </summary>
public partial class ProductUomOptionEditView : UserControl
{
    public ProductUomOptionEditView()
    {
        InitializeComponent();
    }
}
