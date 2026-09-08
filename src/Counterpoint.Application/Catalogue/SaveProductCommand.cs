using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Catalogue;

/// <summary>What <see cref="IProductMaintenance"/> needs to create or edit a product (SRS FR-2.1-FR-2.8).</summary>
/// <param name="Code">The product's unique code.</param>
/// <param name="Name">The product's name.</param>
/// <param name="NameAlt">An alternate-language name, if the shop keeps one.</param>
/// <param name="CategoryId">An existing category, or null for unclassified.</param>
/// <param name="BrandId">An existing brand, or null for unbranded.</param>
/// <param name="BaseUomId">
/// The unit stock is always held in. Fixed at creation - <see cref="IProductMaintenance.UpdateAsync"/>
/// refuses a different value, because changing it would silently redefine every quantity already
/// on the books.
/// </param>
/// <param name="Type">Whole units, fractional, service or non-inventory (FR-2.1-FR-2.8).</param>
/// <param name="TaxClassId">An existing tax class.</param>
/// <param name="Location">Rack or bin, free text.</param>
/// <param name="NonReturnable">True when a sale of this product can never be returned (FR-5).</param>
/// <param name="WarrantyDays">Plain count of days, or null.</param>
/// <param name="Notes">Free text.</param>
/// <param name="MaxDiscountRate">A per-product discount cap, or null to use the shop-wide limit.</param>
/// <param name="ConfirmDuplicate">
/// True to create the product even though <see cref="IProductMaintenance.CreateAsync"/> found an
/// existing product with a very similar name and the same brand (SRS FR-2.24). False - the
/// default - is what the first attempt at any new product should send; a caller that receives
/// <see cref="DuplicateProductWarningException"/> shows the shop what it found and resubmits the
/// same command with this set to proceed. There is no equivalent override for a duplicate
/// <em>barcode</em>: that is FR-2.24's hard-block half, and it never has one.
/// </param>
public sealed record SaveProductCommand(
    string Code,
    string Name,
    string? NameAlt,
    long? CategoryId,
    long? BrandId,
    long BaseUomId,
    ProductType Type,
    long TaxClassId,
    string? Location,
    bool NonReturnable,
    int? WarrantyDays,
    string? Notes,
    Percentage? MaxDiscountRate,
    bool ConfirmDuplicate = false);
