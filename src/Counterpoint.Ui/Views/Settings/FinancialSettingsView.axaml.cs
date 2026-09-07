using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.2. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.FinancialSettingsViewModel"/>.
/// </summary>
public partial class FinancialSettingsView : UserControl
{
    public FinancialSettingsView()
    {
        InitializeComponent();
    }
}
