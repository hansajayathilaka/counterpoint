namespace Counterpoint.Application.Inventory;

/// <summary>
/// The <c>audit_log.action</c> and <c>audit_log.entity_type</c> values <c>PostAdjustmentHandler</c>
/// writes (SRS FR-4, NFR-S8, task P2-T08 "Do this" #3: "every adjustment audited with before/after
/// quantity").
/// </summary>
public static class AdjustmentAuditActions
{
    /// <summary>
    /// There is no <c>adjustment</c> table to file this against - the movement itself is the
    /// document (see <c>PostAdjustmentHandler</c>'s own remarks) - so the audit row is filed
    /// against the variant whose balance changed, the same way <c>ICancelSale</c> files its own
    /// row against the <c>sale</c> it changed.
    /// </summary>
    public const string EntityType = "product_variant";

    public const string AdjustmentPosted = "STOCK_ADJUSTMENT_POSTED";

    public const string DamagePosted = "STOCK_DAMAGE_POSTED";
}
