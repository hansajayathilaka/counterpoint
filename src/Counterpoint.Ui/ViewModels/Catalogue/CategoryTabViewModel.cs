using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The category tab of the catalogue screen (SRS FR-2.20), task P3-T11's proof-of-concept for
/// the shared add/edit/delete dialog (SRS UI-05, UI-06, UI-15, AC-23).
/// </summary>
/// <remarks>
/// The old inline form - one set of fields bound to whichever row was selected, shared by a
/// "_New" button and a "_Save" button with no heading naming the action or the record - is gone.
/// New and Edit each open <see cref="CategoryEditViewModel"/> through <see cref="IDialogService"/>
/// with an explicit <see cref="DialogMode"/>; Delete confirms through the same shell before it
/// does anything, naming the specific category (SRS UI-05).
/// </remarks>
public sealed partial class CategoryTabViewModel : ReferenceDataTabViewModel
{
    private readonly ICategoryMaintenance _categories;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private CategoryRowViewModel? _selectedItem;

    public CategoryTabViewModel(ICategoryMaintenance categories, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(categories);
        ArgumentNullException.ThrowIfNull(dialogService);
        _categories = categories;
        _dialogService = dialogService;
    }

    public ObservableCollection<CategoryRowViewModel> Items { get; } = [];

    /// <summary>Top-level categories, plus "(top level)" itself - the only legal parents (FR-2.20).</summary>
    public ObservableCollection<CategoryParentOption> ParentOptions { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var categories = await _categories.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var category in categories)
                {
                    Items.Add(new CategoryRowViewModel(category));
                }

                ParentOptions.Clear();
                ParentOptions.Add(new CategoryParentOption(null, "(top level)"));
                foreach (var category in categories.Where(c => c.ParentId is null))
                {
                    ParentOptions.Add(new CategoryParentOption(category.Id, category.Name));
                }

                Status = Items.Count == 1 ? "1 category." : Items.Count + " categories.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the shared dialog to create a new category (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task NewAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var content = new CategoryEditViewModel(
                    _categories,
                    ParentOptions,
                    editingId: null,
                    initialName: string.Empty,
                    initialParent: ParentOptions.FirstOrDefault());

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Create,
                    "category",
                    subjectDescription: null,
                    content,
                    cancellationToken).ConfigureAwait(true);

                if (outcome == DialogOutcome.Confirmed)
                {
                    var name = content.Name;
                    await RefreshAsync(cancellationToken).ConfigureAwait(true);
                    Status = name + " created.";
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the shared dialog to edit the selected category (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a category first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var initialParent = ParentOptions.FirstOrDefault(option => option.Id == selected.ParentId)
                    ?? ParentOptions.FirstOrDefault();

                var content = new CategoryEditViewModel(
                    _categories,
                    ParentOptions,
                    editingId: selected.Id,
                    initialName: selected.Name,
                    initialParent: initialParent);

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Edit,
                    "category",
                    subjectDescription: selected.Name,
                    content,
                    cancellationToken).ConfigureAwait(true);

                if (outcome == DialogOutcome.Confirmed)
                {
                    var name = content.Name;
                    await RefreshAsync(cancellationToken).ConfigureAwait(true);
                    Status = name + " updated.";
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a category first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _categories.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _categories.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Confirms through the shared dialog shell, naming the specific category, before deleting it
    /// (SRS UI-05).
    /// </summary>
    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a category first.";
            return;
        }

        var outcome = await _dialogService.ShowDeleteConfirmationAsync(
            "category",
            selected.Name,
            cancellationToken).ConfigureAwait(true);

        if (outcome != DialogOutcome.Confirmed)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                await _categories.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                SelectedItem = null;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }
}
