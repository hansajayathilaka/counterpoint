using Counterpoint.Domain.Catalogue;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Import;

/// <summary>
/// One product's full catalogue export row: everything <see cref="ImportColumnMapping"/> maps a
/// column onto, read back out (SRS FR-2.23). One row per product, exactly as the importer expects
/// one row per product (docs/03_PHASE_1_core_trading.md P1-T13's scope: a spreadsheet catalogue
/// import targets a product's one defining variant, not the variant matrix a multi-attribute
/// product's own editor screen builds - P1-T05).
/// </summary>
/// <param name="Code"><c>product.code</c>, also used as the variant's <c>sku</c> (see remarks).</param>
/// <param name="Price"><c>product_variant.price</c>.</param>
/// <param name="Cost">The variant's current moving-average cost, from the stock balance projection (zero if it has never moved).</param>
/// <param name="QtyOnHand">The variant's current on-hand balance, read off the same projection <c>IStockPositionReader</c> reads (zero if it has never moved) - never summed from the ledger (CLAUDE.md invariant 3).</param>
public sealed record CatalogueExportRow(
    string Code,
    string Name,
    string? NameAlt,
    string? CategoryName,
    string? BrandName,
    string UomName,
    ProductType Type,
    string TaxClassName,
    string? Location,
    bool NonReturnable,
    int? WarrantyDays,
    string? Notes,
    string? PrimaryBarcode,
    Money Price,
    Money Cost,
    Quantity QtyOnHand);
