using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.5. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.PolicySettingsViewModel"/>.
/// </summary>
public partial class PolicySettingsView : UserControl
{
    public PolicySettingsView()
    {
        InitializeComponent();
    }
}
