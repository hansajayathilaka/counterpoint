using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>app_setting</c> (SRS FR-10, docs/01_DATA_MODEL.md §8).
/// </summary>
/// <remarks>
/// <para>
/// A port. The Application layer states what it needs; Counterpoint.Infrastructure supplies the
/// SQLite implementation. Nothing here knows what a key means - that is
/// <c>SettingsSerializer</c>'s business, and keeping the split means the store cannot quietly
/// reinterpret a value on the way past.
/// </para>
/// <para>
/// <b>Writes join the caller's transaction</b>, so the rows and the <c>audit_log</c> entries that
/// explain them commit together or not at all (FR-10.9). There is deliberately no delete: the
/// store only ever touches keys it is handed, which is what lets P1-T02's <c>security.*</c> rows
/// and this framework's rows share one table without either erasing the other.
/// </para>
/// </remarks>
public interface ISettingStore
{
    /// <summary>Every row in the table, keyed by <c>key</c>. Read off a read connection.</summary>
    public Task<IReadOnlyDictionary<string, StoredSetting>> LoadAllAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts or updates exactly the keys given, in the caller's transaction. Keys not named are
    /// left alone.
    /// </summary>
    public Task WriteAsync(
        IReadOnlyList<SettingWrite> writes,
        CancellationToken cancellationToken = default);
}
