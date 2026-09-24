using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Ui.Services;

/// <summary>
/// The one door every add/edit/delete action in the back office walks through (SRS UI-05, UI-06,
/// UI-15, AC-23), task P3-T11.
/// </summary>
/// <remarks>
/// <para>
/// Interface only. <c>Counterpoint.Ui</c> may reference <c>Application</c> and <c>Domain</c>
/// only, never <c>Infrastructure</c>/<c>Devices</c>/<c>Reporting</c>/<c>Backup</c>
/// (CLAUDE.md "Project boundaries"); the concrete <c>AvaloniaDialogService</c> and its DI
/// registration live in <c>Counterpoint.App</c>, the composition root, alongside every other
/// adapter this application wires.
/// </para>
/// <para>
/// Deliberately has no <c>Window</c> (or any other Avalonia type) parameter, even though
/// <c>Counterpoint.Ui</c> already references Avalonia to build its own views: no
/// <c>Counterpoint.Ui.ViewModels.*</c> type references <c>Avalonia.Controls</c> today (the sales,
/// catalogue and settings viewmodels all raise events and let the view layer - <c>App.axaml.cs</c>
/// - decide which window owns which), and a caller that had to hold its own owning
/// <c>Window</c> just to open a dialog would be the first to break that. The implementation
/// resolves the current top-level window itself (the active window inside the single desktop
/// lifetime this single-till application ever runs) and shows the dialog modal only to it -
/// never to the whole application, so a back-office dialog never blocks <c>SalesWindow</c>.
/// </para>
/// </remarks>
public interface IDialogService
{
    /// <summary>
    /// Shows the shared <c>EditDialogWindow</c> shell hosting <paramref name="content"/>. The
    /// header reads "New {<paramref name="entityName"/>}" for <see cref="DialogMode.Create"/> or
    /// "Edit {<paramref name="entityName"/>} — {<paramref name="subjectDescription"/>}" for
    /// <see cref="DialogMode.Edit"/> - driven only by <paramref name="mode"/>, never inferred from
    /// whatever <paramref name="content"/> currently holds.
    /// </summary>
    /// <typeparam name="TViewModel">
    /// The content viewmodel type the dialog hosts. Must derive from
    /// <see cref="ViewModels.ViewModelBase"/> so the application's <see cref="ViewLocator"/> can
    /// find its view by the existing "...ViewModel" → "...View" naming convention, and must
    /// implement <see cref="IEditDialogContent"/> so the shell can drive Save.
    /// </typeparam>
    /// <param name="mode">Explicit: Create or Edit. See <see cref="DialogMode"/>.</param>
    /// <param name="entityName">
    /// The plain-language name of what is being created or edited, e.g. <c>"category"</c>.
    /// </param>
    /// <param name="subjectDescription">
    /// Identifies the specific record being edited, e.g. its name. Ignored for
    /// <see cref="DialogMode.Create"/>.
    /// </param>
    /// <param name="content">The already-constructed content viewmodel.</param>
    /// <param name="cancellationToken">
    /// Requesting cancellation closes the dialog as though Cancel had been pressed.
    /// </param>
    /// <returns>
    /// <see cref="DialogOutcome.Confirmed"/> once <paramref name="content"/>'s
    /// <see cref="IEditDialogContent.SaveAsync"/> has returned <see langword="true"/>;
    /// <see cref="DialogOutcome.Cancelled"/> otherwise.
    /// </returns>
    public Task<DialogOutcome> ShowEditDialogAsync<TViewModel>(
        DialogMode mode,
        string entityName,
        string? subjectDescription,
        TViewModel content,
        CancellationToken cancellationToken = default)
        where TViewModel : ViewModels.ViewModelBase, IEditDialogContent;

    /// <summary>
    /// Shows the same shell as a Confirm/Cancel delete confirmation that names the specific
    /// record before anything happens (SRS UI-05) - never a bare "Are you sure?". The caller
    /// performs the actual delete only after this returns <see cref="DialogOutcome.Confirmed"/>.
    /// </summary>
    /// <param name="entityName">The plain-language name of what is being deleted, e.g. <c>"category"</c>.</param>
    /// <param name="subjectDescription">Identifies the specific record, e.g. its name.</param>
    /// <param name="cancellationToken">
    /// Requesting cancellation closes the dialog as though Cancel had been pressed.
    /// </param>
    public Task<DialogOutcome> ShowDeleteConfirmationAsync(
        string entityName,
        string subjectDescription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Shows the same shell as a generic Confirm/Cancel dialog naming, in plain language, what a
    /// caller-supplied action will do before it happens (SRS UI-05) - task P3-T19's own use is the
    /// back-office shell's navigate-away-from-System-with-unsaved-settings-changes guard, which
    /// names what will be discarded the same way <see cref="ShowDeleteConfirmationAsync"/> already
    /// names what will be deleted. Not specific to deletion or to settings - any caller needing a
    /// plain confirm-before-acting dialog uses this rather than inventing a new one.
    /// </summary>
    /// <param name="headerText">The dialog's own header, e.g. <c>"Discard unsaved settings changes?"</c>.</param>
    /// <param name="message">The plain sentence naming what will happen if confirmed.</param>
    /// <param name="confirmButtonText">
    /// The primary button's own label, e.g. <c>"_Discard and leave"</c> - never a bare "OK", so the
    /// operator always reads what pressing it does.
    /// </param>
    /// <param name="cancellationToken">
    /// Requesting cancellation closes the dialog as though Cancel had been pressed.
    /// </param>
    public Task<DialogOutcome> ShowConfirmationAsync(
        string headerText,
        string message,
        string confirmButtonText,
        CancellationToken cancellationToken = default);
}
