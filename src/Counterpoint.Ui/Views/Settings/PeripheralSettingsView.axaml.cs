using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.6. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.PeripheralSettingsViewModel"/>.
/// </summary>
public partial class PeripheralSettingsView : UserControl
{
    public PeripheralSettingsView()
    {
        InitializeComponent();
    }
}
