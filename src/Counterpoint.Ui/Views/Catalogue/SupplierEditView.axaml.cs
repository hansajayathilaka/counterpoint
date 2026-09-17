using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The supplier create/edit dialog content. Markup and nothing else: every behaviour it has is a
/// binding to <see cref="ViewModels.Catalogue.SupplierEditViewModel"/>.
/// </summary>
public partial class SupplierEditView : UserControl
{
    public SupplierEditView()
    {
        InitializeComponent();
    }
}
