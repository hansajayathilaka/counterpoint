using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T15's dialog when creating or editing one customer (SRS FR-6.1, UI-15,
/// AC-23), following <see cref="CategoryEditViewModel"/>'s pattern exactly.
/// </summary>
public sealed partial class CustomerEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly ICustomerMaintenance _customers;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _phone;

    [ObservableProperty]
    private string _address;

    [ObservableProperty]
    private string _taxNo;

    [ObservableProperty]
    private bool _isTrade;

    [ObservableProperty]
    private string _creditLimitText;

    public CustomerEditViewModel(
        ICustomerMaintenance customers,
        long? editingId,
        string initialName,
        string initialPhone,
        string initialAddress,
        string initialTaxNo,
        bool isTrade,
        string initialCreditLimitText)
    {
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(initialName);
        ArgumentNullException.ThrowIfNull(initialPhone);
        ArgumentNullException.ThrowIfNull(initialAddress);
        ArgumentNullException.ThrowIfNull(initialTaxNo);
        ArgumentNullException.ThrowIfNull(initialCreditLimitText);

        _customers = customers;
        _editingId = editingId;
        _name = initialName;
        _phone = initialPhone;
        _address = initialAddress;
        _taxNo = initialTaxNo;
        _isTrade = isTrade;
        _creditLimitText = initialCreditLimitText;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var command = new SaveCustomerCommand(
            Name, Phone, Address, TaxNo, IsTrade ? "TRADE" : "RETAIL", SettingsText.ToMoney(CreditLimitText));

        if (_editingId is { } id)
        {
            await _customers.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _customers.CreateAsync(command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }
}
