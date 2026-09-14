namespace Counterpoint.Application.Inventory;

/// <summary>
/// The <c>audit_log.action</c> and <c>audit_log.entity_type</c> values
/// <c>PostBulkBreakHandler</c> writes (SRS FR-4.9, NFR-S8, task P2-T09).
/// </summary>
public static class BulkBreakAuditActions
{
    /// <summary>The <c>bulk_break</c> header row this audit entry describes.</summary>
    public const string EntityType = "bulk_break";

    public const string Posted = "BULK_BREAK_POSTED";
}
