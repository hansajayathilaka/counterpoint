using System.Collections.Generic;
using System.Linq;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>What <see cref="IDatabaseSnapshotSource.VerifyRawCopyAsync"/> found.</summary>
/// <param name="IntegrityOk">True when <c>PRAGMA integrity_check</c> returned exactly <c>ok</c>.</param>
/// <param name="IntegrityMessage">
/// The raw <c>PRAGMA integrity_check</c> result, or a plain-language reason the file could not be
/// opened at all (for example, the wrong database key).
/// </param>
/// <param name="RowCountsByTable">Every user table's row count, keyed by table name.</param>
public sealed record DatabaseVerificationResult(
    bool IntegrityOk,
    string IntegrityMessage,
    IReadOnlyDictionary<string, long> RowCountsByTable)
{
    /// <summary>Total rows across every table - the plain "open and count rows" figure (SRS FR-11.4).</summary>
    public long TotalRowCount => RowCountsByTable.Values.Sum();
}
