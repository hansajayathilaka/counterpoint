using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The back office's persistent left navigation rail (tasks P3-T18/P3-T19, SRS UI-11, UI-13,
/// UI-16): Overview, Catalogue (its eight sections, content-pane-swapping), Trading, People and
/// System (its nine settings groups, content-pane-swapping since task P3-T19). Markup and nothing
/// else - every command it fires already exists on
/// <see cref="ViewModels.BackOfficeShellViewModel"/>, unchanged from before these tasks except for
/// <c>ManageCatalogueCommand</c> (retired by P3-T18, Catalogue no longer opens a window) and
/// <c>OpenSettingsCommand</c> (retired by P3-T19, Settings no longer opens a window either).
/// </summary>
public partial class NavRail : UserControl
{
    public NavRail()
    {
        InitializeComponent();
    }
}
