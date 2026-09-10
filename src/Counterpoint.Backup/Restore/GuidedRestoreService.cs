using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Backup.Snapshots;

namespace Counterpoint.Backup.Restore;

/// <summary>
/// Implements <see cref="IGuidedRestoreService"/>: the guided restore wizard's backend (SRS
/// FR-11.12, FR-11.13, P1-T15).
/// </summary>
/// <remarks>
/// <para>
/// Internal, like every concrete service behind an interface carrying
/// <see cref="RequiresRoleAttribute"/> - the composition root builds one, decorates it, and hands
/// out only the decorated <see cref="IGuidedRestoreService"/> (SRS NFR-S2, AC-17).
/// </para>
/// <para>
/// <see cref="RestoreService"/> stays exactly what its own remarks say it is: a low-level reversal
/// of <see cref="SnapshotService"/> that only ever reads a file path it is given. Everything FR-
/// 11.12 asks for beyond that - the typed confirmation, backing up the current database first, the
/// audit entry, and staging the result instead of touching the live database file - lives here.
/// </para>
/// </remarks>
internal sealed class GuidedRestoreService : IGuidedRestoreService
{
    private readonly RestoreService _restoreService;
    private readonly SnapshotService _snapshotService;
    private readonly SnapshotOptions _options;
    private readonly IAuditTrail _audit;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public GuidedRestoreService(
        RestoreService restoreService,
        SnapshotService snapshotService,
        SnapshotOptions options,
        IAuditTrail audit,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(restoreService);
        ArgumentNullException.ThrowIfNull(snapshotService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _restoreService = restoreService;
        _snapshotService = snapshotService;
        _options = options;
        _audit = audit;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<GuidedRestorePreview> PreviewAsync(string backupFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            var preview = await RestoreService.PreviewAsync(backupFilePath, cancellationToken).ConfigureAwait(false);
            return new GuidedRestorePreview(preview.TakenAt, preview.SchemaVersion);
        }
        catch (BackupRestoreException exception)
        {
            // BackupRestoreException is Counterpoint.Backup's own type - Counterpoint.Ui may not
            // reference this assembly (CLAUDE.md "Project boundaries"), so nothing more specific
            // than InvalidOperationException may cross IGuidedRestoreService's boundary. The
            // message is already plain language; SettingsViewModel already knows how to show one.
            throw new InvalidOperationException(exception.Message, exception);
        }
    }

    /// <inheritdoc />
    public async Task<GuidedRestoreOutcome> RestoreAsync(GuidedRestoreRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.Equals(request.TypedConfirmation, GuidedRestoreConfirmation.RequiredPhrase, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Type " + GuidedRestoreConfirmation.RequiredPhrase + " exactly to confirm. Nothing has been changed.",
                nameof(request));
        }

        // Verify the checksum and read the data date before anything else - FR-11.12's own order,
        // and cheaper than the safety backup below, so a damaged or wrong file is refused first.
        BackupPreview preview;
        try
        {
            preview = await RestoreService.PreviewAsync(request.BackupFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (BackupRestoreException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }

        // FR-11.12: "back up the current database before overwriting it." This is the till's own,
        // already-tested snapshot pipeline, run against whatever is live right now - a genuinely
        // separate backup from the one being restored, encrypted under the shop's own backup
        // passphrase, not the restore's.
        SnapshotResult safetyBackup;
        try
        {
            safetyBackup = await _snapshotService.CreateSnapshotAsync(usbDirectory: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException)
        {
            throw new InvalidOperationException(
                "The current database could not be backed up first, so nothing has been restored: "
                    + exception.Message,
                exception);
        }

        var stagingPath = PendingRestoreLocation.StagingFilePath(_options.SnapshotDirectory);

        try
        {
            await _restoreService.RestoreAsync(request.BackupFilePath, request.Passphrase, stagingPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (BackupRestoreException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }

        var occurredAt = _timeProvider.GetUtcNow();

        await _audit.RecordAsync(
            new AuditEntry(
                occurredAt,
                _session.CurrentUser?.Id,
                BackupAuditActions.DatabaseRestored,
                BackupAuditActions.BackupRecordEntityType,
                EntityId: null,
                Reason: "Restored " + Path.GetFileName(request.BackupFilePath)
                    + " (data date " + preview.TakenAt.ToString("O") + "); safety backup "
                    + safetyBackup.Filename + " taken first. Takes effect on the next start."),
            cancellationToken).ConfigureAwait(false);

        return new GuidedRestoreOutcome(safetyBackup.Filename, preview.SchemaVersion, preview.TakenAt);
    }
}
