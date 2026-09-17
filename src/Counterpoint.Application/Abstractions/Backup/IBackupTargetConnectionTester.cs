using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// Tests whether an off-site backup target can be reached, for the settings screen's "Test
/// connection" button (SRS FR-11.5, P4-T01).
/// </summary>
/// <remarks>
/// <para>
/// A port, for the same reason <see cref="IDatabaseSnapshotSource"/> and
/// <see cref="IBackupRecordStore"/> are: <c>Counterpoint.Backup</c> knows how to build and test an
/// <c>IBackupTarget</c>, but <c>Counterpoint.Ui</c> may not reference <c>Counterpoint.Backup</c>
/// directly (CLAUDE.md "Project boundaries"). The composition root hands this interface to the
/// settings screen; the implementation lives in <c>Counterpoint.Backup.Targets</c>.
/// </para>
/// <para>
/// No <c>RequiresRoleAttribute</c>: testing a connection reads a target's credential only far
/// enough to attempt one round trip and never returns the credential itself, the same
/// "reading is open" reasoning <c>IBackupPassphraseStore.HasPassphrase</c> and <c>ISettings</c>'s
/// own read side carry. The settings screen that hosts the button is reached through
/// <c>SalesViewModel.CanChangeSettings</c> in the first place (owner-only at the UI convenience
/// level; CLAUDE.md invariant 8 already puts the real guard - <c>ISettings.SaveAsync</c> and
/// <see cref="IBackupTargetCredentialStore"/>'s writes - in the Application layer).
/// </para>
/// </remarks>
public interface IBackupTargetConnectionTester
{
    /// <summary>
    /// Tries to reach <paramref name="target"/> and confirms whatever credential is available can
    /// upload, list and download - without changing anything the shop would notice, and without
    /// ever calling delete.
    /// </summary>
    /// <param name="target">Which target to test.</param>
    /// <param name="credentialOverride">
    /// When set, tests this credential instead of whatever is already stored - so the owner can
    /// try a freshly typed credential before saving it. Null or empty tests the stored one.
    /// </param>
    public Task<BackupTargetConnectionResult> TestConnectionAsync(
        CloudBackupTarget target,
        string? credentialOverride = null,
        CancellationToken cancellationToken = default);
}
