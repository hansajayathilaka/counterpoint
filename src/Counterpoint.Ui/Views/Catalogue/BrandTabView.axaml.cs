using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The brand tab. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Catalogue.BrandTabViewModel"/>.
/// </summary>
public partial class BrandTabView : UserControl
{
    public BrandTabView()
    {
        InitializeComponent();
    }
}
