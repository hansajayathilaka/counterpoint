using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The print queue screen (P1-T11): pending and failed jobs, with a retry button. Markup and
/// nothing else - every behaviour it has is a binding to <see cref="ViewModels.PrintQueueViewModel"/>.
/// </summary>
public partial class PrintQueueWindow : Window
{
    public PrintQueueWindow()
    {
        InitializeComponent();
    }
}
