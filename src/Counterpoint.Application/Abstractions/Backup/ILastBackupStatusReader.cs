using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// Reads the most recent <c>backup_record</c> row, for the dashboard's "last backup status"
/// (SRS FR-9.7) and the status bar (UI-09).
/// </summary>
/// <remarks>
/// The read half of <see cref="IBackupRecordStore"/>, which is write-only by design. Nothing
/// writes a backup yet in this task's dependency set - local and USB backup are P1-T15 - so this
/// honestly answers null on a fresh till until then, rather than fabricating a status.
/// </remarks>
public interface ILastBackupStatusReader
{
    /// <summary>The most recent backup, or null when none has ever been taken.</summary>
    public Task<LastBackupStatus?> GetLastAsync(CancellationToken cancellationToken = default);
}
