using System;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// One row per applied migration. The migration runner (P0-T04) compares this against the
/// migrations shipped in the assembly to decide whether the database needs upgrading (DM-05).
/// </summary>
/// <remarks>
/// Matches the <c>schema_version</c> DDL in docs/01_DATA_MODEL.md: <c>version TEXT PRIMARY KEY</c>,
/// <c>applied_at TEXT NOT NULL</c>.
/// </remarks>
public sealed class SchemaVersion
{
    /// <summary>Migration identifier, for example <c>0001_Skeleton</c>.</summary>
    public required string Version { get; init; }

    /// <summary>When the migration was applied. Stored as ISO-8601 text.</summary>
    public required DateTimeOffset AppliedAt { get; init; }
}
