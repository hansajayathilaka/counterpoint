namespace Counterpoint.Application.Purchasing;

/// <summary>
/// The <c>audit_log.action</c> and <c>audit_log.entity_type</c> values <see cref="GoodsReceiptService"/>
/// writes (SRS FR-4.7, NFR-S8).
/// </summary>
public static class GoodsReceiptAuditActions
{
    public const string EntityType = "goods_receipt";

    public const string Created = "GOODS_RECEIPT_CREATED";
}
