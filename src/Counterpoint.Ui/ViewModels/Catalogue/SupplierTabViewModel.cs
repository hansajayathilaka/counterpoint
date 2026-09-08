using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The supplier tab of the catalogue screen (SRS FR-6.5).</summary>
public sealed partial class SupplierTabViewModel : ReferenceDataTabViewModel
{
    private readonly ISupplierMaintenance _suppliers;
    private long? _editingId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _contact = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _taxNo = string.Empty;

    [ObservableProperty]
    private string _paymentTerms = string.Empty;

    [ObservableProperty]
    private SupplierRowViewModel? _selectedItem;

    public SupplierTabViewModel(ISupplierMaintenance suppliers)
    {
        ArgumentNullException.ThrowIfNull(suppliers);
        _suppliers = suppliers;
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

    [RelayCommand]
    public void New()
    {
        _editingId = null;
        SelectedItem = null;
        Name = string.Empty;
        Contact = string.Empty;
        Phone = string.Empty;
        Address = string.Empty;
        TaxNo = string.Empty;
        PaymentTerms = string.Empty;
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var command = new SaveSupplierCommand(Name, Contact, Phone, Address, TaxNo, PaymentTerms);

                if (_editingId is { } id)
                {
                    await _suppliers.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " updated.";
                }
                else
                {
                    await _suppliers.CreateAsync(command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " created.";
                }

                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
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

    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a supplier first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _suppliers.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(SupplierRowViewModel? value)
    {
        _editingId = value?.Id;
        Name = value?.Name ?? string.Empty;
        Contact = value?.Contact ?? string.Empty;
        Phone = value?.Phone ?? string.Empty;
        Address = value?.Address ?? string.Empty;
        TaxNo = value?.TaxNo ?? string.Empty;
        PaymentTerms = value?.PaymentTerms ?? string.Empty;
    }
}
