using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The tax-class create/edit dialog content. Markup and nothing else: every behaviour it has is a
/// binding to <see cref="ViewModels.Catalogue.TaxClassEditViewModel"/>.
/// </summary>
public partial class TaxClassEditView : UserControl
{
    public TaxClassEditView()
    {
        InitializeComponent();
    }
}
