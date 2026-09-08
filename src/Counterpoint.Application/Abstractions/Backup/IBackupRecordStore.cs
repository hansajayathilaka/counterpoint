using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>Records a completed backup in <c>backup_record</c> (docs/01_DATA_MODEL.md §8).</summary>
/// <remarks>
/// Called only after the encrypted file is already safely on disk under its final name - see
/// <c>Counterpoint.Backup.Snapshots.SnapshotService</c> for why that ordering matters: a crash
/// before the file is finished leaves nothing to record; a crash after leaves a finished file with
/// no row, which a later listing can simply discover, rather than a row pointing at a file that
/// was never completed.
/// </remarks>
public interface IBackupRecordStore
{
    /// <summary>Appends one row. There is no update - a backup record describes a fact, not a state.</summary>
    public Task RecordAsync(NewBackupRecord record, CancellationToken cancellationToken = default);
}
