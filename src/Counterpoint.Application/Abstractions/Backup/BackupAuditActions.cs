namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>The <c>audit_log.action</c> values <see cref="IGuidedRestoreService"/> writes (SRS FR-11.13).</summary>
public static class BackupAuditActions
{
    /// <summary>A guided restore staged a backup to take effect on the next start.</summary>
    public const string DatabaseRestored = "DATABASE_RESTORED";

    /// <summary>The <c>backup_record</c> table, as <c>audit_log.entity_type</c>.</summary>
    public const string BackupRecordEntityType = "backup_record";
}
