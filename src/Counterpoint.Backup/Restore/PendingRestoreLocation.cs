using System.IO;

namespace Counterpoint.Backup.Restore;

/// <summary>
/// Where a guided restore stages the decrypted database until the till restarts (SRS FR-11.12,
/// P1-T15).
/// </summary>
/// <remarks>
/// A single-instance, single-connection till has no safe way to swap its own live database file
/// out from under itself while it is running (CLAUDE.md "single named-mutex instance"), so a
/// restore does not touch <c>db/counterpoint.db</c> directly. It decrypts to this well-known path
/// instead, and <c>Counterpoint.App.Program</c> - the only project that can see both this project
/// and <c>Counterpoint.Infrastructure</c>'s <c>PosDataDirectory</c> - checks for it before running
/// a single migration on the next start, moves it into place, and deletes any stale write-ahead
/// log sidecar files the database it is replacing left behind.
/// </remarks>
public static class PendingRestoreLocation
{
    private const string FolderName = "pending-restore";
    private const string StagedFileName = "counterpoint.db";

    /// <summary>
    /// The staged file's path under <paramref name="snapshotDirectory"/> -
    /// <c>Counterpoint.Infrastructure.Data.PosDataDirectory.SnapshotDirectory</c>, passed in
    /// because <c>Counterpoint.Backup</c> may not reference <c>Counterpoint.Infrastructure</c>
    /// itself (CLAUDE.md "Project boundaries").
    /// </summary>
    public static string StagingFilePath(string snapshotDirectory) =>
        Path.Combine(snapshotDirectory, FolderName, StagedFileName);
}
