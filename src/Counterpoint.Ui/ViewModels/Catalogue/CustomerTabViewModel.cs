using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The customer tab of the catalogue screen (SRS FR-6.1).</summary>
/// <remarks>
/// Task P3-T15: converted onto the P3-T11 dialog shell following the Category screen's
/// proof-of-concept exactly. New and Edit each open <see cref="CustomerEditViewModel"/> through
/// <see cref="IDialogService"/> with an explicit <see cref="DialogMode"/>; the old inline form -
/// five watermark-only fields task P3-T12's own tests used as the "still unconverted" fixture -
/// is gone. Delete confirms through the same shell before it does anything, naming the specific
/// customer (SRS UI-05).
/// </remarks>
public sealed partial class CustomerTabViewModel : ReferenceDataTabViewModel
{
    private readonly ICustomerMaintenance _customers;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private CustomerRowViewModel? _selectedItem;

    public CustomerTabViewModel(ICustomerMaintenance customers, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(dialogService);
        _customers = customers;
        _dialogService = dialogService;
    }

    public ObservableCollection<CustomerRowViewModel> Items { get; } = [];

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var customers = await _customers.ListAsync(cancellationToken).ConfigureAwait(true);

                Items.Clear();
                foreach (var customer in customers)
                {
                    Items.Add(new CustomerRowViewModel(customer));
                }

                Status = Items.Count == 1 ? "1 customer." : Items.Count + " customers.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Opens the shared dialog to create a new customer (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task NewAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var content = new CustomerEditViewModel(
                    _customers, editingId: null, "", "", "", "", isTrade: false, "0");

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Create,
                    "customer",
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

    /// <summary>Opens the shared dialog to edit the selected customer (SRS UI-15, AC-23).</summary>
    [RelayCommand]
    public async Task EditAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a customer first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var content = new CustomerEditViewModel(
                    _customers,
                    selected.Id,
                    selected.Name,
                    selected.Phone,
                    selected.Address,
                    selected.TaxNo,
                    selected.Type == "TRADE",
                    selected.CreditLimitText);

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Edit,
                    "customer",
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
            Status = "Pick a customer first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _customers.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _customers.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Confirms through the shared dialog shell, naming the specific customer, before deleting it
    /// (SRS UI-05).
    /// </summary>
    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a customer first.";
            return;
        }

        var outcome = await _dialogService.ShowDeleteConfirmationAsync(
            "customer",
            selected.Name,
            cancellationToken).ConfigureAwait(true);

        if (outcome != DialogOutcome.Confirmed)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                await _customers.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                SelectedItem = null;
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }
}
