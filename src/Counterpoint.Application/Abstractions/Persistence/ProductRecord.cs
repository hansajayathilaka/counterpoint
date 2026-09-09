using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One row of <c>product</c>, with the reference-data names a screen needs to display it
/// (docs/01_DATA_MODEL.md §3, SRS FR-2.1-FR-2.8).
/// </summary>
/// <remarks>
/// Deliberately cost-free. <see cref="IProductStore.FindByIdAsync"/> is not owner-gated on its
/// own - it is also reachable through <c>IStockEnquiry</c>, which any cashier session can call
/// (CLAUDE.md invariant 8). Owner-only reads that need cost use their own purpose-built type
/// instead, e.g. <c>PriceQueryVariant</c> for <c>IPriceQuery</c>, or a narrow single-value read
/// such as <c>IProductStore.FindCostAvgAsync</c> for the below-cost check in
/// <c>ProductMaintenanceService</c>, which is called only from the owner-gated
/// <c>IProductMaintenance</c>.
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
    bool Active);
