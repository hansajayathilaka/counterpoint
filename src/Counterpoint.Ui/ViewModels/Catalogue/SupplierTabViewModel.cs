using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The supplier tab of the catalogue screen (SRS FR-6.5).</summary>
/// <remarks>
/// Task P3-T15: converted onto the P3-T11 dialog shell following the Category screen's
/// proof-of-concept exactly. New and Edit each open <see cref="SupplierEditViewModel"/> through
/// <see cref="IDialogService"/> with an explicit <see cref="DialogMode"/>; the old inline form is
/// gone. Delete confirms through the same shell before it does anything, naming the specific
/// supplier (SRS UI-05).
/// </remarks>
public sealed partial class SupplierTabViewModel : ReferenceDataTabViewModel
{
    private readonly ISupplierMaintenance _suppliers;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private SupplierRowViewModel? _selectedItem;

    public SupplierTabViewModel(ISupplierMaintenance suppliers, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(dialogService);
        _suppliers = suppliers;
        _dialogService = dialogService;
    }

    public ObservableCollection<SupplierRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var suppliers = await _suppliers.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var supplier in suppliers)
                {
                    Items.Add(new SupplierRowViewModel(supplier));
                }

                Status = Items.Count == 1 ? "1 supplier." : Items.Count + " suppliers.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the shared dialog to create a new supplier (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task NewAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var content = new SupplierEditViewModel(_suppliers, editingId: null, "", "", "", "", "", "");

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Create,
                    "supplier",
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

    /// <summary>Opens the shared dialog to edit the selected supplier (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a supplier first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var content = new SupplierEditViewModel(
                    _suppliers,
                    selected.Id,
                    selected.Name,
                    selected.Contact,
                    selected.Phone,
                    selected.Address,
                    selected.TaxNo,
                    selected.PaymentTerms);

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Edit,
                    "supplier",
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
            Status = "Pick a supplier first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _suppliers.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _suppliers.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Confirms through the shared dialog shell, naming the specific supplier, before deleting it
    /// (SRS UI-05).
    /// </summary>
    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a supplier first.";
            return;
        }

        var outcome = await _dialogService.ShowDeleteConfirmationAsync(
            "supplier",
            selected.Name,
            cancellationToken).ConfigureAwait(true);

        if (outcome != DialogOutcome.Confirmed)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                await _suppliers.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                SelectedItem = null;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }
}
