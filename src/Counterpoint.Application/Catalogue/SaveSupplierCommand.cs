namespace Counterpoint.Application.Catalogue;

/// <summary>What <see cref="ISupplierMaintenance"/> needs to create or edit a supplier (FR-6.5).</summary>
public sealed record SaveSupplierCommand(
    string Name,
    string? Contact,
    string? Phone,
    string? Address,
    string? TaxNo,
    string? PaymentTerms);
