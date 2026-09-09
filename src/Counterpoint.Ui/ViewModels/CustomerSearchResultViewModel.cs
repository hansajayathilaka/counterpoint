using System;
using Counterpoint.Application.Abstractions.Persistence;

namespace Counterpoint.Ui.ViewModels;

/// <summary>One row of the F8 customer search result list (SRS FR-3.22 - "by name or phone").</summary>
public sealed class CustomerSearchResultViewModel
{
    public CustomerSearchResultViewModel(CustomerRecord customer)
    {
        ArgumentNullException.ThrowIfNull(customer);

        Id = customer.Id;
        Name = customer.Name;
        Phone = customer.Phone ?? string.Empty;
    }

    public long Id { get; }

    public string Name { get; }

    public string Phone { get; }
}
