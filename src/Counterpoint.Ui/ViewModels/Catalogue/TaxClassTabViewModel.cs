using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The tax-class tab of the catalogue screen (Q-02, FR-10.3).</summary>
/// <remarks>
/// Task P3-T15: converted onto the P3-T11 dialog shell following the Category screen's
/// proof-of-concept exactly. New and Edit each open <see cref="TaxClassEditViewModel"/> through
/// <see cref="IDialogService"/> with an explicit <see cref="DialogMode"/>; the old inline form is
/// gone. Delete confirms through the same shell before it does anything, naming the specific tax
/// class (SRS UI-05).
/// </remarks>
public sealed partial class TaxClassTabViewModel : ReferenceDataTabViewModel
{
    private readonly ITaxClassMaintenance _taxClasses;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private TaxClassRowViewModel? _selectedItem;

    public TaxClassTabViewModel(ITaxClassMaintenance taxClasses, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(taxClasses);
        ArgumentNullException.ThrowIfNull(dialogService);
        _taxClasses = taxClasses;
        _dialogService = dialogService;
    }

    public ObservableCollection<TaxClassRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var taxClasses = await _taxClasses.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var taxClass in taxClasses)
                {
                    Items.Add(new TaxClassRowViewModel(taxClass));
                }

                Status = Items.Count == 1 ? "1 tax class." : Items.Count + " tax classes.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the shared dialog to create a new tax class (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task NewAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var content = new TaxClassEditViewModel(_taxClasses, editingId: null, "", "0");

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Create,
                    "tax class",
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

    /// <summary>Opens the shared dialog to edit the selected tax class (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a tax class first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var content = new TaxClassEditViewModel(
                    _taxClasses, selected.Id, selected.Name, selected.RatePercentText);

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Edit,
                    "tax class",
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
            Status = "Pick a tax class first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _taxClasses.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _taxClasses.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Confirms through the shared dialog shell, naming the specific tax class, before deleting it
    /// (SRS UI-05).
    /// </summary>
    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a tax class first.";
            return;
        }

        var outcome = await _dialogService.ShowDeleteConfirmationAsync(
            "tax class",
            selected.Name,
            cancellationToken).ConfigureAwait(true);

        if (outcome != DialogOutcome.Confirmed)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                await _taxClasses.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                SelectedItem = null;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }
}
