using System;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels.Catalogue;

/// <summary>One row of the supplier list (SRS FR-6.5).</summary>
public sealed class SupplierRowViewModel
{
    public SupplierRowViewModel(SupplierRecord supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);

        Id = supplier.Id;
        Name = supplier.Name;
        Contact = supplier.Contact ?? string.Empty;
        Phone = supplier.Phone ?? string.Empty;
        Address = supplier.Address ?? string.Empty;
        TaxNo = supplier.TaxNo ?? string.Empty;
        PaymentTerms = supplier.PaymentTerms ?? string.Empty;
        Active = supplier.Active;
        StateText = supplier.Active ? "active" : "off";
    }

    public long Id { get; }

    public string Name { get; }

    public string Contact { get; }

    public string Phone { get; }

    public string Address { get; }

    public string TaxNo { get; }

    public string PaymentTerms { get; }

    public bool Active { get; }

    public string StateText { get; }
}
