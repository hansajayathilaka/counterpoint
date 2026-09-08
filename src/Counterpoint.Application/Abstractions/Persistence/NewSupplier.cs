namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>The fields <see cref="ISupplierStore"/> writes, on a create or an update alike.</summary>
public sealed record NewSupplier(
    string Name,
    string? Contact,
    string? Phone,
    string? Address,
    string? TaxNo,
    string? PaymentTerms);
