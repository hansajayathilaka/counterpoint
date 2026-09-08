using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One row of <c>product</c>, with the reference-data names a screen needs to display it
/// (docs/01_DATA_MODEL.md §3, SRS FR-2.1-FR-2.8).
/// </summary>
/// <remarks>
/// Carries <see cref="CostAvg"/>. That is safe only because <c>IProductMaintenance</c>, the one
/// port this record is returned from, is owner-only end to end (SRS §3.3 ROLE-2, NFR-S2, AC-17) -
/// the same reasoning that lets <c>Counterpoint.Application.Inventory.StockEnquiryResult</c> carry
/// cost. It is not the cashier-facing catalogue read: that is
/// <c>Counterpoint.Application.Abstractions.Persistence.CatalogueItem</c>, and it stays cost-free
/// (CLAUDE.md invariant 8).
/// </remarks>
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
    bool Active,
    Money CostAvg);
