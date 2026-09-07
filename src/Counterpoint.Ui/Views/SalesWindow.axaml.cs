using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The sales screen. Markup and nothing else: every behaviour it has is a binding to
/// <see cref="ViewModels.SalesViewModel"/>.
/// </summary>
public partial class SalesWindow : Window
{
    public SalesWindow()
    {
        InitializeComponent();
    }
}
