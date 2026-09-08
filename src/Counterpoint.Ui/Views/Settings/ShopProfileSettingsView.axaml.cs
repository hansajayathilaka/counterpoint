using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.1. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.ShopProfileSettingsViewModel"/>.
/// </summary>
public partial class ShopProfileSettingsView : UserControl
{
    public ShopProfileSettingsView()
    {
        InitializeComponent();
    }
}
