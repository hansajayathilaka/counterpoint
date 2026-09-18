using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The customer create/edit dialog content. Markup and nothing else: every behaviour it has is a
/// binding to <see cref="ViewModels.Catalogue.CustomerEditViewModel"/>.
/// </summary>
public partial class CustomerEditView : UserControl
{
    public CustomerEditView()
    {
        InitializeComponent();
    }
}
