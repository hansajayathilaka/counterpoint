using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// One off-site place a backup can be copied to - passive and interchangeable, so the choice
/// between them (Q-D) is reversible and the terminal holds the least possible privilege (SRS
/// FR-11.5, Q-09, SAD ADR-003, P4-T01).
/// </summary>
/// <remarks>
/// <para>
/// <b>Narrow on purpose.</b> Five operations, nothing about retry, backoff, scheduling or
/// throttling - that is <c>UploadWorker</c>'s job (P4-T03), built on top of this interface, not
/// inside an implementation of it. An implementation only ever talks to its one target; it knows
/// nothing about <c>backup_record</c>, the write connection, or when it is being called.
/// </para>
/// <para>
/// <b><see cref="DeleteAsync"/> exists for the retention pruner P4-T04 builds, not for anything in
/// this task.</b> Nothing in <c>Counterpoint.Backup</c>'s own wiring calls it automatically today
/// - the terminal must not be able to delete its own off-site backups (P4-T02's least-privilege
/// setup is what actually enforces that, structurally, with a credential that cannot). Only a
/// human pressing an explicit "remove this backup" control, or the portal-side pruning job P4-T04
/// builds, should ever reach it.
/// </para>
/// <para>
/// <b>TLS is never optional.</b> Every implementation talks HTTPS through the framework's default
/// certificate validation; none of them exposes a way to disable it, not even for a test (CLAUDE.md
/// invariant, this task's own item 5).
/// </para>
/// </remarks>
public interface IBackupTarget
{
    /// <summary>
    /// Uploads <paramref name="content"/> under <paramref name="key"/>, with <paramref name="metadata"/>
    /// attached however the target stores metadata. Overwrites an object already at that key.
    /// </summary>
    /// <exception cref="BackupTargetException">The upload could not complete - see <see cref="BackupTargetException.Kind"/>.</exception>
    public Task UploadAsync(
        Stream content,
        string key,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every object whose key starts with <paramref name="prefix"/>. Empty prefix lists everything.</summary>
    /// <exception cref="BackupTargetException">The listing could not complete.</exception>
    public Task<IReadOnlyList<BackupObjectInfo>> ListAsync(
        string prefix,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads the object at <paramref name="key"/>. The caller owns and disposes the returned stream.</summary>
    /// <exception cref="BackupTargetException">The object does not exist, or the download could not complete.</exception>
    public Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the object at <paramref name="key"/>. See this interface's own remarks: nothing in
    /// this task wires an automatic caller to this method.
    /// </summary>
    /// <exception cref="BackupTargetException">The object could not be removed.</exception>
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Confirms the target can be reached and its credential accepted, without uploading, listing
    /// for anyone's benefit but its own check, or deleting anything. Never throws - a connection
    /// problem is exactly what this call exists to report.
    /// </summary>
    public Task<BackupTargetConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}
