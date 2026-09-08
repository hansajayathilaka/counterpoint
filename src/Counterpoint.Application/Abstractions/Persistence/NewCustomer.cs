using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>The fields <see cref="ICustomerStore"/> writes, on a create or an update alike.</summary>
public sealed record NewCustomer(
    string Name,
    string? Phone,
    string? Address,
    string? TaxNo,
    string Type,
    Money CreditLimit);
