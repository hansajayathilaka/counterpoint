using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.3. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.TaxSettingsViewModel"/>.
/// </summary>
public partial class TaxSettingsView : UserControl
{
    public TaxSettingsView()
    {
        InitializeComponent();
    }
}
