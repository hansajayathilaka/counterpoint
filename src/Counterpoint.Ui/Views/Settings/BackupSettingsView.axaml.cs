using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.7. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.BackupSettingsViewModel"/>.
/// </summary>
public partial class BackupSettingsView : UserControl
{
    public BackupSettingsView()
    {
        InitializeComponent();
    }
}
