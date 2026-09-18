using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The brand tab of the catalogue screen (SRS FR-2.21).</summary>
/// <remarks>
/// Task P3-T15: converted onto the P3-T11 dialog shell following the Category screen's
/// proof-of-concept exactly. New and Edit each open <see cref="BrandEditViewModel"/> through
/// <see cref="IDialogService"/> with an explicit <see cref="DialogMode"/>; the old inline form -
/// one set of fields bound to whichever row was selected, shared by a "_New" button and a "_Save"
/// button with nothing on screen naming the action or the record - is gone. Delete confirms
/// through the same shell before it does anything, naming the specific brand (SRS UI-05).
/// </remarks>
public sealed partial class BrandTabViewModel : ReferenceDataTabViewModel
{
    private readonly IBrandMaintenance _brands;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private BrandRowViewModel? _selectedItem;

    public BrandTabViewModel(IBrandMaintenance brands, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(brands);
        ArgumentNullException.ThrowIfNull(dialogService);
        _brands = brands;
        _dialogService = dialogService;
    }

    public ObservableCollection<BrandRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var brands = await _brands.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var brand in brands)
                {
                    Items.Add(new BrandRowViewModel(brand));
                }

                Status = Items.Count == 1 ? "1 brand." : Items.Count + " brands.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the shared dialog to create a new brand (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task NewAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var content = new BrandEditViewModel(_brands, editingId: null, initialName: string.Empty);

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Create,
                    "brand",
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

    /// <summary>Opens the shared dialog to edit the selected brand (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a brand first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var content = new BrandEditViewModel(_brands, selected.Id, selected.Name);

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Edit,
                    "brand",
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
            Status = "Pick a brand first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _brands.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _brands.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Confirms through the shared dialog shell, naming the specific brand, before deleting it
    /// (SRS UI-05).
    /// </summary>
    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a brand first.";
            return;
        }

        var outcome = await _dialogService.ShowDeleteConfirmationAsync(
            "brand",
            selected.Name,
            cancellationToken).ConfigureAwait(true);

        if (outcome != DialogOutcome.Confirmed)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                await _brands.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                SelectedItem = null;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }
}
