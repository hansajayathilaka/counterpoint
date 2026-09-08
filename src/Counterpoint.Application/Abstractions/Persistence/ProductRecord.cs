using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>product</c>, with the reference-data names a screen needs to display it (docs/01_DATA_MODEL.md §3, SRS FR-2.1-FR-2.8).</summary>
public sealed record ProductRecord(
    long Id,
    string Code,
    string Name,
    string? NameAlt,
    long? CategoryId,
    string? CategoryName,
    long? BrandId,
    string? BrandName,
    long BaseUomId,
    string BaseUomSymbol,
    ProductType Type,
    long TaxClassId,
    string TaxClassName,
    string? Location,
    bool NonReturnable,
    int? WarrantyDays,
    string? Notes,
    Percentage? MaxDiscountRate,
    bool Active);
