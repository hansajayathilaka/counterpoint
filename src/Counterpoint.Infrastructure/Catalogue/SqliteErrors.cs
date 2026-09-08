using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Counterpoint.Infrastructure.Catalogue;

/// <summary>
/// Recognises the two shapes of <c>SQLITE_CONSTRAINT</c> (error code 19) the reference-data
/// stores can hit on a write: a business-rule trigger's own <c>RAISE(ABORT, …)</c>, and a plain
/// foreign-key violation. Both come back from EF Core as a <see cref="DbUpdateException"/>
/// wrapping a <see cref="SqliteException"/>; this is where that wrapping is unwrapped, once,
/// rather than in every store.
/// </summary>
internal static class SqliteErrors
{
    /// <summary>
    /// True when a <c>RAISE(ABORT, …)</c> trigger fired and its message contains
    /// <paramref name="fragment"/> - for example the two-level category guard's
    /// "two levels only" (docs/01_DATA_MODEL.md, <c>trg_category_two_levels_*</c>).
    /// </summary>
    internal static bool RaisedByTrigger(DbUpdateException exception, string fragment) =>
        exception.InnerException is SqliteException sqlite
        && sqlite.SqliteErrorCode == SqliteConstraintErrorCode
        && sqlite.Message.Contains(fragment, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the database's own foreign keys refused the write - the backstop for a
    /// reference a store's application-layer pre-check did not see (CLAUDE.md invariant 9,
    /// <c>foreign_keys=ON</c>).
    /// </summary>
    internal static bool IsForeignKeyViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException sqlite
        && sqlite.SqliteErrorCode == SqliteConstraintErrorCode
        && sqlite.Message.Contains("FOREIGN KEY constraint failed", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>SQLite's <c>SQLITE_CONSTRAINT</c> primary result code. Both shapes above use it.</summary>
    private const int SqliteConstraintErrorCode = 19;
}
