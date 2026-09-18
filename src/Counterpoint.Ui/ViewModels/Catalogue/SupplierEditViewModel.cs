using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Catalogue;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>
/// The content of task P3-T15's dialog when creating or editing one supplier (SRS FR-6.5, UI-15,
/// AC-23), following <see cref="CategoryEditViewModel"/>'s pattern exactly.
/// </summary>
public sealed partial class SupplierEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly ISupplierMaintenance _suppliers;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _contact;

    [ObservableProperty]
    private string _phone;

    [ObservableProperty]
    private string _address;

    [ObservableProperty]
    private string _taxNo;

    [ObservableProperty]
    private string _paymentTerms;

    public SupplierEditViewModel(
        ISupplierMaintenance suppliers,
        long? editingId,
        string initialName,
        string initialContact,
        string initialPhone,
        string initialAddress,
        string initialTaxNo,
        string initialPaymentTerms)
    {
        ArgumentNullException.ThrowIfNull(suppliers);
        ArgumentNullException.ThrowIfNull(initialName);
        ArgumentNullException.ThrowIfNull(initialContact);
        ArgumentNullException.ThrowIfNull(initialPhone);
        ArgumentNullException.ThrowIfNull(initialAddress);
        ArgumentNullException.ThrowIfNull(initialTaxNo);
        ArgumentNullException.ThrowIfNull(initialPaymentTerms);

        _suppliers = suppliers;
        _editingId = editingId;
        _name = initialName;
        _contact = initialContact;
        _phone = initialPhone;
        _address = initialAddress;
        _taxNo = initialTaxNo;
        _paymentTerms = initialPaymentTerms;
    }

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        var command = new SaveSupplierCommand(Name, Contact, Phone, Address, TaxNo, PaymentTerms);

        if (_editingId is { } id)
        {
            await _suppliers.UpdateAsync(id, command, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _suppliers.CreateAsync(command, cancellationToken).ConfigureAwait(true);
        }

        return true;
    }
}
