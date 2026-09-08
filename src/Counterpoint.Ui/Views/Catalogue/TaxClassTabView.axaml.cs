using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The tax-class tab. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Catalogue.TaxClassTabViewModel"/>.
/// </summary>
public partial class TaxClassTabView : UserControl
{
    public TaxClassTabView()
    {
        InitializeComponent();
    }
}
