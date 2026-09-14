namespace Counterpoint.Application.Inventory;

/// <summary>
/// The <c>audit_log.action</c> and <c>audit_log.entity_type</c> values <see cref="StockTakeService"/>
/// writes (SRS FR-4 stock take, NFR-S8, task P2-T10).
/// </summary>
public static class StockTakeAuditActions
{
    public const string EntityType = "stock_take";

    /// <summary>A count sheet was generated (SRS FR-4 stock take).</summary>
    public const string Started = "STOCK_TAKE_STARTED";

    /// <summary>Corrections were posted as one batch (SRS AC-10).</summary>
    public const string Posted = "STOCK_TAKE_POSTED";

    /// <summary>A stock take was abandoned; nothing was posted.</summary>
    public const string Abandoned = "STOCK_TAKE_ABANDONED";
}
