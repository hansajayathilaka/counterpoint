using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The unit-of-measure tab of the catalogue screen.</summary>
/// <remarks>
/// Task P3-T15: converted onto the P3-T11 dialog shell following the Category screen's
/// proof-of-concept exactly. New and Edit each open <see cref="UomEditViewModel"/> through
/// <see cref="IDialogService"/> with an explicit <see cref="DialogMode"/>; the old inline form is
/// gone. Delete confirms through the same shell before it does anything, naming the specific unit
/// (SRS UI-05).
/// </remarks>
public sealed partial class UomTabViewModel : ReferenceDataTabViewModel
{
    private readonly IUomMaintenance _uoms;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private UomRowViewModel? _selectedItem;

    public UomTabViewModel(IUomMaintenance uoms, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(uoms);
        ArgumentNullException.ThrowIfNull(dialogService);
        _uoms = uoms;
        _dialogService = dialogService;
    }

    public ObservableCollection<UomRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var uoms = await _uoms.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var uom in uoms)
                {
                    Items.Add(new UomRowViewModel(uom));
                }

                Status = Items.Count == 1 ? "1 unit." : Items.Count + " units.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the shared dialog to create a new unit (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task NewAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var content = new UomEditViewModel(_uoms, editingId: null, "", "", "0");

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Create,
                    "unit",
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

    /// <summary>Opens the shared dialog to edit the selected unit (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a unit first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var content = new UomEditViewModel(
                    _uoms,
                    selected.Id,
                    selected.Name,
                    selected.Symbol,
                    selected.DecimalPlaces.ToString(System.Globalization.CultureInfo.InvariantCulture));

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Edit,
                    "unit",
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
            Status = "Pick a unit first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _uoms.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _uoms.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Confirms through the shared dialog shell, naming the specific unit, before deleting it
    /// (SRS UI-05).
    /// </summary>
    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a unit first.";
            return;
        }

        var outcome = await _dialogService.ShowDeleteConfirmationAsync(
            "unit",
            selected.Name,
            cancellationToken).ConfigureAwait(true);

        if (outcome != DialogOutcome.Confirmed)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                await _uoms.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                SelectedItem = null;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }
}
