using Avalonia.Controls;

namespace Counterpoint.Ui.Views;

/// <summary>
/// The back office's own window (task P3-T13, SRS UI-11). Nothing but markup - navigation is the
/// composition root's job (see <c>App.axaml.cs</c>'s <c>ShowBackOffice</c>), and everything a
/// tile here leads to re-checks the session's role at the Application layer regardless of what
/// this window shows or hides (SRS NFR-S2, AC-17, AC-24).
/// </summary>
public partial class BackOfficeShellWindow : Window
{
    public BackOfficeShellWindow()
    {
        InitializeComponent();
    }
}
