using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Integration.Tests.Ui;

/// <summary>
/// A test double for <see cref="IDialogService"/>. A real <c>EditDialogWindow</c> cannot be
/// opened in CI (<see cref="Sales.SaleFixture"/>'s own remarks), so this drives the same contract
/// a real dialog would - it records exactly what it was asked to show, which is what proves task
/// P3-T11's screens pass an explicit <see cref="DialogMode"/> and record identity rather than
/// inferring one from a field - and then answers as configured, as though an operator had acted.
/// </summary>
internal sealed class FakeDialogService : IDialogService
{
    /// <summary>Every call this fake received, in order - the proof that a screen asked for the right mode and subject.</summary>
    internal System.Collections.Generic.List<(DialogMode Mode, string EntityName, string? SubjectDescription)> EditRequests { get; } = [];

    /// <summary>Every delete confirmation this fake received, in order, naming the record asked about.</summary>
    internal System.Collections.Generic.List<(string EntityName, string SubjectDescription)> DeleteRequests { get; } = [];

    /// <summary>Every generic confirmation (task P3-T19) this fake received, in order.</summary>
    internal System.Collections.Generic.List<(string HeaderText, string Message, string ConfirmButtonText)> ConfirmationRequests { get; } = [];

    /// <summary>When true (the default), <see cref="ShowEditDialogAsync{TViewModel}"/> calls the content's Save as though the operator pressed Save.</summary>
    internal bool ConfirmEdits { get; set; } = true;

    /// <summary>When true (the default), <see cref="ShowDeleteConfirmationAsync"/> answers as though the operator pressed Confirm.</summary>
    internal bool ConfirmDeletes { get; set; } = true;

    /// <summary>When true (the default), <see cref="ShowConfirmationAsync"/> answers as though the operator pressed the confirm button.</summary>
    internal bool ConfirmConfirmations { get; set; } = true;

    /// <summary>
    /// Stands in for whatever an operator would type into the dialog's fields before pressing
    /// Save - run against the content viewmodel <see cref="ShowEditDialogAsync{TViewModel}"/> was
    /// given, immediately before it is saved.
    /// </summary>
    internal System.Action<object>? ConfigureContent { get; set; }

    public async Task<DialogOutcome> ShowEditDialogAsync<TViewModel>(
        DialogMode mode,
        string entityName,
        string? subjectDescription,
        TViewModel content,
        CancellationToken cancellationToken = default)
        where TViewModel : ViewModelBase, IEditDialogContent
    {
        EditRequests.Add((mode, entityName, subjectDescription));

        if (!ConfirmEdits)
        {
            return DialogOutcome.Cancelled;
        }

        ConfigureContent?.Invoke(content);

        var saved = await content.SaveAsync(cancellationToken).ConfigureAwait(true);
        return saved ? DialogOutcome.Confirmed : DialogOutcome.Cancelled;
    }

    public Task<DialogOutcome> ShowDeleteConfirmationAsync(
        string entityName,
        string subjectDescription,
        CancellationToken cancellationToken = default)
    {
        DeleteRequests.Add((entityName, subjectDescription));
        return Task.FromResult(ConfirmDeletes ? DialogOutcome.Confirmed : DialogOutcome.Cancelled);
    }

    public Task<DialogOutcome> ShowConfirmationAsync(
        string headerText,
        string message,
        string confirmButtonText,
        CancellationToken cancellationToken = default)
    {
        ConfirmationRequests.Add((headerText, message, confirmButtonText));
        return Task.FromResult(ConfirmConfirmations ? DialogOutcome.Confirmed : DialogOutcome.Cancelled);
    }
}
