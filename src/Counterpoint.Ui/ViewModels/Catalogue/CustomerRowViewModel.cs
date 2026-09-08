using System;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Ui.ViewModels;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of the customer list (SRS FR-6.1).</summary>
public sealed class CustomerRowViewModel
{
    public CustomerRowViewModel(CustomerRecord customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        Id = customer.Id;
        Name = customer.Name;
        Phone = customer.Phone ?? string.Empty;
        Address = customer.Address ?? string.Empty;
        TaxNo = customer.TaxNo ?? string.Empty;
        Type = customer.Type;
        CreditLimitText = SettingsText.FromMoney(customer.CreditLimit);
        Active = customer.Active;
        StateText = customer.Active ? "active" : "off";
    }

    public long Id { get; }

    public string Name { get; }

    public string Phone { get; }

    public string Address { get; }

    public string TaxNo { get; }

    public string Type { get; }

    public string CreditLimitText { get; }

    public bool Active { get; }

    public string StateText { get; }
}
