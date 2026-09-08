using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Catalogue;

/// <summary>
/// The customer tab. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Catalogue.CustomerTabViewModel"/>.
/// </summary>
public partial class CustomerTabView : UserControl
{
    public CustomerTabView()
    {
        InitializeComponent();
    }
}
