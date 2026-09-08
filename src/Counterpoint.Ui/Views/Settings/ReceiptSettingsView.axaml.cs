using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.8. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.ReceiptSettingsViewModel"/>.
/// </summary>
public partial class ReceiptSettingsView : UserControl
{
    public ReceiptSettingsView()
    {
        InitializeComponent();
    }
}
