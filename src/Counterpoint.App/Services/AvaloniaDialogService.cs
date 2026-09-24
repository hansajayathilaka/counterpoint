using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Dialogs;
using Counterpoint.Ui.Views;

namespace Counterpoint.App.Services;

/// <summary>
/// The composition root's implementation of task P3-T11's <see cref="IDialogService"/>
/// (SRS UI-05, UI-06, UI-15, AC-23). Lives here, not in <c>Counterpoint.Ui</c>, per CLAUDE.md's
/// project-boundary rule: <c>Ui</c> may reference <c>Application</c> and <c>Domain</c> only, and
/// <c>App</c> is the one project allowed to see both <c>Ui</c> and the Avalonia desktop lifetime
/// this class resolves the owning window from.
/// </summary>
internal sealed class AvaloniaDialogService : IDialogService
{
    public Task<DialogOutcome> ShowEditDialogAsync<TViewModel>(
        DialogMode mode,
        string entityName,
        string? subjectDescription,
        TViewModel content,
        CancellationToken cancellationToken = default)
        where TViewModel : ViewModelBase, IEditDialogContent
    {
        var viewModel = EditDialogWindowViewModel.ForEdit(mode, entityName, subjectDescription, content);
        return ShowAsync(viewModel, cancellationToken);
    }

    public Task<DialogOutcome> ShowDeleteConfirmationAsync(
        string entityName,
        string subjectDescription,
        CancellationToken cancellationToken = default)
    {
        var viewModel = EditDialogWindowViewModel.ForDelete(entityName, subjectDescription);
        return ShowAsync(viewModel, cancellationToken);
    }

    public Task<DialogOutcome> ShowConfirmationAsync(
        string headerText,
        string message,
        string confirmButtonText,
        CancellationToken cancellationToken = default)
    {
        var viewModel = EditDialogWindowViewModel.ForConfirmation(headerText, message, confirmButtonText);
        return ShowAsync(viewModel, cancellationToken);
    }

    /// <summary>
    /// Shows <paramref name="viewModel"/> in an <see cref="EditDialogWindow"/>, modal only to
    /// whichever window is currently active in this application's one desktop lifetime - never to
    /// the whole application, so a back-office dialog never blocks <c>SalesWindow</c>. Falls back
    /// to <see cref="IClassicDesktopStyleApplicationLifetime.MainWindow"/> when nothing is
    /// currently active (for example, a dialog opened programmatically before a click has ever
    /// given a window focus), and shows non-modally as a last resort when no window exists yet at
    /// all - the caller's own screen never has that shape in production, but a caller misused
    /// this way still gets a working dialog rather than an exception.
    /// </summary>
    private static async Task<DialogOutcome> ShowAsync(
        EditDialogWindowViewModel viewModel,
        CancellationToken cancellationToken)
    {
        var window = new EditDialogWindow { DataContext = viewModel };

        using var registration = cancellationToken.CanBeCanceled
            ? cancellationToken.Register(() => Avalonia.Threading.Dispatcher.UIThread.Post(() => viewModel.CancelCommand.Execute(null)))
            : default;

        var owner = ResolveOwner();
        if (owner is not null)
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }
        else
        {
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            await closed.Task.ConfigureAwait(true);
        }

        return viewModel.Outcome;
    }

    private static Window? ResolveOwner()
    {
        // Fully qualified: "Application" on its own binds to the Counterpoint.Application
        // namespace (also referenced here), not Avalonia's own Application class.
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return null;
        }

        return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
    }
}
