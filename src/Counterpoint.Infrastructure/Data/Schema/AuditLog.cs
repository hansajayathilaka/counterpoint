namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>audit_log</c>. APPEND ONLY, hash chained. See Schema/README.md.</summary>
internal sealed class AuditLog
{
    public long Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public long? UserId { get; set; }

    /// <summary>For example <c>SALE_CANCELLED</c>, <c>PRICE_CHANGED</c>.</summary>
    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public long? EntityId { get; set; }

    public string? BeforeJson { get; set; }

    public string? AfterJson { get; set; }

    public string? Reason { get; set; }

    public string PrevHash { get; set; } = string.Empty;

    public string RowHash { get; set; } = string.Empty;
}
