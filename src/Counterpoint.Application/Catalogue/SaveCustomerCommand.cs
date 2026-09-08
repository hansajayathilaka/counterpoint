using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>What <see cref="ICustomerMaintenance"/> needs to create or edit a customer (FR-6.1).</summary>
/// <param name="Type"><c>RETAIL</c> or <c>TRADE</c> (<c>ck_customer_type</c>).</param>
public sealed record SaveCustomerCommand(
    string Name,
    string? Phone,
    string? Address,
    string? TaxNo,
    string Type,
    Money CreditLimit);
