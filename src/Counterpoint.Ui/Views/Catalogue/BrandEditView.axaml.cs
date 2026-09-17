using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The brand create/edit dialog content. Markup and nothing else: every behaviour it has is a
/// binding to <see cref="ViewModels.Catalogue.BrandEditViewModel"/>.
/// </summary>
public partial class BrandEditView : UserControl
{
    public BrandEditView()
    {
        InitializeComponent();
    }
}
