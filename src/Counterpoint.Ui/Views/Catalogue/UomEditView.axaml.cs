using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The unit-of-measure create/edit dialog content. Markup and nothing else: every behaviour it
/// has is a binding to <see cref="ViewModels.Catalogue.UomEditViewModel"/>.
/// </summary>
public partial class UomEditView : UserControl
{
    public UomEditView()
    {
        InitializeComponent();
    }
}
