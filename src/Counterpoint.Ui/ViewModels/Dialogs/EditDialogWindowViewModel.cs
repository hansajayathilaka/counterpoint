using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Security;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Dialogs;

/// <summary>
/// Drives task P3-T11's one shared add/edit/delete dialog shell, <c>EditDialogWindow</c>
/// (SRS UI-05, UI-06, UI-15, AC-23).
/// </summary>
/// <remarks>
/// Every piece of text this exposes - <see cref="HeaderText"/>, <see cref="PrimaryButtonText"/>,
/// <see cref="Message"/> - is fixed at construction from the explicit
/// <see cref="DialogMode"/>/entity name/subject description the caller passed to
/// <see cref="IDialogService"/>. Nothing here inspects the content viewmodel's fields to decide
/// what to say; that inference (the same inline form shared by a "_New" button and a "_Save"
/// button, with nothing on screen saying which) was the confirmed defect this task replaces.
/// </remarks>
public sealed partial class EditDialogWindowViewModel : ViewModelBase
{
    private readonly Func<CancellationToken, Task<bool>> _primaryAction;

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private string? _errorMessage;

    private EditDialogWindowViewModel(
        string headerText,
        string primaryButtonText,
        string cancelButtonText,
        string? message,
        object? content,
        Func<CancellationToken, Task<bool>> primaryAction)
    {
        HeaderText = headerText;
        PrimaryButtonText = primaryButtonText;
        CancelButtonText = cancelButtonText;
        Message = message;
        Content = content;
        _primaryAction = primaryAction;
    }

    /// <summary>
    /// "New {entity}" for <see cref="DialogMode.Create"/>, "Edit {entity} — {subject}" for
    /// <see cref="DialogMode.Edit"/>, or "Delete {entity}" for a delete confirmation - computed
    /// once, from the explicit values the factory methods below were given.
    /// </summary>
    public string HeaderText { get; }

    public string PrimaryButtonText { get; }

    public string CancelButtonText { get; }

    /// <summary>
    /// The plain sentence a delete confirmation or a generic <see cref="ForConfirmation"/> dialog
    /// shows (SRS UI-05); <see langword="null"/> for a create/edit dialog, which shows
    /// <see cref="Content"/> instead.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// The caller's own content viewmodel for a create/edit dialog, resolved to a view through the
    /// application's existing <see cref="ViewLocator"/>; <see langword="null"/> for a delete or
    /// generic confirmation, which shows <see cref="Message"/> instead.
    /// </summary>
    public object? Content { get; }

    /// <summary>
    /// True for a delete confirmation or a generic <see cref="ForConfirmation"/> dialog - the
    /// footer/body renders <see cref="Message"/> rather than <see cref="Content"/>. The name
    /// predates task P3-T19's generic confirmation and is kept unchanged (every existing binding
    /// and test already reads it) rather than renamed for a cosmetic-only reason.
    /// </summary>
    public bool IsDeleteConfirmation => Message is not null;

    /// <summary>
    /// What the operator did. <see cref="DialogOutcome.Cancelled"/> until
    /// <see cref="PrimaryCommand"/> succeeds.
    /// </summary>
    public DialogOutcome Outcome { get; private set; } = DialogOutcome.Cancelled;

    /// <summary>Raised once <see cref="Outcome"/> is decided; the view closes the window.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>An edit dialog: header driven by <paramref name="mode"/>, footer is Save/Cancel.</summary>
    public static EditDialogWindowViewModel ForEdit<TViewModel>(
        DialogMode mode,
        string entityName,
        string? subjectDescription,
        TViewModel content)
        where TViewModel : ViewModelBase, IEditDialogContent
    {
        ArgumentNullException.ThrowIfNull(entityName);
        ArgumentNullException.ThrowIfNull(content);

        var header = mode == DialogMode.Create
            ? "New " + entityName
            : "Edit " + entityName + " — " + subjectDescription;

        return new EditDialogWindowViewModel(
            header,
            primaryButtonText: "_Save",
            cancelButtonText: "_Cancel",
            message: null,
            content: content,
            primaryAction: content.SaveAsync);
    }

    /// <summary>
    /// A delete confirmation reusing the same shell: footer is Confirm/Cancel, and the body names
    /// the specific record before anything happens (SRS UI-05).
    /// </summary>
    public static EditDialogWindowViewModel ForDelete(string entityName, string subjectDescription)
    {
        ArgumentNullException.ThrowIfNull(entityName);
        ArgumentNullException.ThrowIfNull(subjectDescription);

        var message = "Delete " + entityName + " \"" + subjectDescription + "\"? This cannot be undone.";

        return new EditDialogWindowViewModel(
            headerText: "Delete " + entityName,
            primaryButtonText: "_Delete",
            cancelButtonText: "_Cancel",
            message: message,
            content: null,
            primaryAction: static _ => Task.FromResult(true));
    }

    /// <summary>
    /// A generic Confirm/Cancel dialog reusing the same shell (task P3-T19, SRS UI-05): footer is
    /// <paramref name="confirmButtonText"/>/Cancel, and the body names what confirming will do -
    /// the same "name it before anything happens" shape <see cref="ForDelete"/> already uses,
    /// generalised past deletion (the back-office shell's navigate-away-from-System guard names a
    /// discarded settings edit, not a deleted record).
    /// </summary>
    public static EditDialogWindowViewModel ForConfirmation(
        string headerText, string message, string confirmButtonText)
    {
        ArgumentNullException.ThrowIfNull(headerText);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(confirmButtonText);

        return new EditDialogWindowViewModel(
            headerText,
            primaryButtonText: confirmButtonText,
            cancelButtonText: "_Cancel",
            message: message,
            content: null,
            primaryAction: static _ => Task.FromResult(true));
    }

    [RelayCommand]
    private async Task PrimaryAsync(CancellationToken cancellationToken)
    {
        Busy = true;
        ErrorMessage = null;
        try
        {
            var done = await _primaryAction(cancellationToken).ConfigureAwait(true);
            if (done)
            {
                Outcome = DialogOutcome.Confirmed;
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (NotAuthorisedException exception)
        {
            // The Application layer refused. Shown, not worked around (SRS UI-06, NFR-S2) - the
            // dialog stays open so the operator sees why nothing was saved.
            ErrorMessage = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        Outcome = DialogOutcome.Cancelled;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
