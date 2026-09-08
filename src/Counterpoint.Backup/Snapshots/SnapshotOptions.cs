namespace Counterpoint.Backup.Snapshots;

/// <param name="SnapshotDirectory">
/// Folder the encrypted backup file is written to. The composition root passes
/// <c>PosDataDirectory.SnapshotDirectory</c>; <c>Counterpoint.Backup</c> never resolves it itself -
/// it may not reference <c>Counterpoint.Infrastructure</c>, where <c>PosDataDirectory</c> lives
/// (CLAUDE.md "Project boundaries").
/// </param>
public sealed record SnapshotOptions(string SnapshotDirectory);
