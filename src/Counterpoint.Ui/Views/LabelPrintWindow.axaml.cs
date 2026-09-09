using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The owner's label-printing screen (SRS FR-2.10, FR-2.12). Markup and nothing else - every
/// behaviour it has is a binding to <see cref="ViewModels.Labels.LabelPrintViewModel"/>.
/// </summary>
public partial class LabelPrintWindow : Window
{
    public LabelPrintWindow()
    {
        InitializeComponent();
    }
}
