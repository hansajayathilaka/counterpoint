using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The unit-of-measure tab. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Catalogue.UomTabViewModel"/>.
/// </summary>
public partial class UomTabView : UserControl
{
    public UomTabView()
    {
        InitializeComponent();
    }
}
