using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The back office's persistent left navigation rail (task P3-T18, SRS UI-11, UI-16): Overview,
/// Catalogue (its eight sections, content-pane-swapping), Trading, People and System. Markup and
/// nothing else - every command it fires already exists on
/// <see cref="ViewModels.BackOfficeShellViewModel"/>, unchanged from before this task except for
/// <c>ManageCatalogueCommand</c>, retired because Catalogue no longer opens a window.
/// </summary>
public partial class NavRail : UserControl
{
    public NavRail()
    {
        InitializeComponent();
    }
}
