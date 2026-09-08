using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>The customer tab of the catalogue screen (SRS FR-6.1).</summary>
public sealed partial class CustomerTabViewModel : ReferenceDataTabViewModel
{
    private readonly ICustomerMaintenance _customers;
    private long? _editingId;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _taxNo = string.Empty;

    [ObservableProperty]
    private bool _isTrade;

    [ObservableProperty]
    private string _creditLimitText = "0";

    [ObservableProperty]
    private CustomerRowViewModel? _selectedItem;

    public CustomerTabViewModel(ICustomerMaintenance customers)
    {
        ArgumentNullException.ThrowIfNull(customers);
        _customers = customers;
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

    [RelayCommand]
    public void New()
    {
        _editingId = null;
        SelectedItem = null;
        Name = string.Empty;
        Phone = string.Empty;
        Address = string.Empty;
        TaxNo = string.Empty;
        IsTrade = false;
        CreditLimitText = "0";
    }

    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var command = new SaveCustomerCommand(
                    Name, Phone, Address, TaxNo, IsTrade ? "TRADE" : "RETAIL", SettingsText.ToMoney(CreditLimitText));

                if (_editingId is { } id)
                {
                    await _customers.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
                    Status = Name + " updated.";
                }
                else
                {
                    await _customers.CreateAsync(command, cancellationToken).ConfigureAwait(true);
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

    [RelayCommand]
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (SelectedItem is not { } selected)
        {
            Status = "Pick a customer first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _customers.DeleteAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                New();
                await RefreshAsync(cancellationToken).ConfigureAwait(true);
                Status = selected.Name + " deleted.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    partial void OnSelectedItemChanged(CustomerRowViewModel? value)
    {
        _editingId = value?.Id;
        Name = value?.Name ?? string.Empty;
        Phone = value?.Phone ?? string.Empty;
        Address = value?.Address ?? string.Empty;
        TaxNo = value?.TaxNo ?? string.Empty;
        IsTrade = value?.Type == "TRADE";
        CreditLimitText = value?.CreditLimitText ?? "0";
    }
}
