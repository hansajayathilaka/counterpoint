using System;
using System.Collections.Generic;

namespace Counterpoint.Backup.Restore;

/// <summary>What one call to <see cref="RestoreService.RestoreAsync"/> produced.</summary>
/// <param name="RestoredFilePath">
/// Where the raw, still SQLCipher-encrypted, database file was written.
/// </param>
/// <param name="SchemaVersion">The schema version recorded in the backup's header.</param>
/// <param name="TakenAt">When the backup was originally taken.</param>
/// <param name="RowCountsByTable">Every user table's row count in the restored database.</param>
/// <param name="TotalRowCount">Total rows across every table.</param>
public sealed record RestoreResult(
    string RestoredFilePath,
    string SchemaVersion,
    DateTimeOffset TakenAt,
    IReadOnlyDictionary<string, long> RowCountsByTable,
    long TotalRowCount);
