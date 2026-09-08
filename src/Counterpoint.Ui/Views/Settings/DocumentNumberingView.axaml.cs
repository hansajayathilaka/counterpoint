using Avalonia.Controls;

namespace Counterpoint.Ui.Views.Settings;

/// <summary>
/// FR-10.4, one document series. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.Settings.DocumentNumberingViewModel"/>.
/// </summary>
public partial class DocumentNumberingView : UserControl
{
    public DocumentNumberingView()
    {
        InitializeComponent();
    }
}
