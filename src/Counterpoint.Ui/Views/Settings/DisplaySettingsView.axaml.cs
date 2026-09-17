using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// UI-13, NFR-U4. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.DisplaySettingsViewModel"/>.
/// </summary>
public partial class DisplaySettingsView : UserControl
{
    public DisplaySettingsView()
    {
        InitializeComponent();
    }
}
