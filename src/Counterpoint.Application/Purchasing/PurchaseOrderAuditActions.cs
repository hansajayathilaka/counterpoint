namespace Counterpoint.Application.Purchasing;

/// <summary>
/// The <c>audit_log.action</c> and <c>audit_log.entity_type</c> values <see cref="PurchaseOrderService"/>
/// writes (SRS FR-4.5, NFR-S8).
/// </summary>
public static class PurchaseOrderAuditActions
{
    public const string EntityType = "purchase_order";

    public const string Created = "PURCHASE_ORDER_CREATED";
    public const string Sent = "PURCHASE_ORDER_SENT";
    public const string Cancelled = "PURCHASE_ORDER_CANCELLED";
    public const string Printed = "PURCHASE_ORDER_PRINTED";
    public const string StatusRecomputed = "PURCHASE_ORDER_STATUS_RECOMPUTED";
}
