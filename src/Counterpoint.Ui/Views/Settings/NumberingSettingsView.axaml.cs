using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.4. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.NumberingSettingsViewModel"/>.
/// </summary>
public partial class NumberingSettingsView : UserControl
{
    public NumberingSettingsView()
    {
        InitializeComponent();
    }
}
