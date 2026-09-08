namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>supplier</c> (docs/01_DATA_MODEL.md §4, FR-6.5).</summary>
public sealed record SupplierRecord(
    long Id,
    string Name,
    string? Contact,
    string? Phone,
    string? Address,
    string? TaxNo,
    string? PaymentTerms,
    bool Active);
