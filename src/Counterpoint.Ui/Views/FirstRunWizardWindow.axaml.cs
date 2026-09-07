using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The first-run setup wizard (SRS FR-10, FR-1.3). Markup and nothing else: every behaviour it
/// has is a binding to <see cref="ViewModels.FirstRun.FirstRunWizardViewModel"/>, and the window
/// that follows it is the composition root's decision.
/// </summary>
public partial class FirstRunWizardWindow : Window
{
    public FirstRunWizardWindow()
    {
        InitializeComponent();
    }
}
