namespace Counterpoint.Application.Catalogue;

/// <summary>
/// The <c>audit_log.action</c> and <c>audit_log.entity_type</c> values the reference-data
/// screens write (SRS FR-2.20, FR-2.21, FR-6, NFR-S8).
/// </summary>
/// <remarks>
/// Named constants rather than literals at the call sites, for the same reason
/// <c>SecurityAuditActions</c> is: the audit-log viewer (P3-T08) filters on exactly these
/// strings.
/// </remarks>
public static class CatalogueAuditActions
{
    public const string CategoryEntityType = "category";
    public const string BrandEntityType = "brand";
    public const string UomEntityType = "uom";
    public const string TaxClassEntityType = "tax_class";
    public const string SupplierEntityType = "supplier";
    public const string CustomerEntityType = "customer";

    public const string Created = "CATALOGUE_ITEM_CREATED";
    public const string Updated = "CATALOGUE_ITEM_UPDATED";
    public const string Deactivated = "CATALOGUE_ITEM_DEACTIVATED";
    public const string Reactivated = "CATALOGUE_ITEM_REACTIVATED";
    public const string Deleted = "CATALOGUE_ITEM_DELETED";
}
