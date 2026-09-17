using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Settings;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// The settings screen's "Test connection" button (SRS FR-11.5, P4-T01) - builds whichever target
/// <see cref="BackupTargetFactory"/> can from the current settings and credential, and asks it to
/// prove it can be reached.
/// </summary>
/// <remarks>
/// Internal: nothing outside <c>Counterpoint.Backup</c> needs the concrete type, only the
/// <see cref="IBackupTargetConnectionTester"/> the composition root registers, the same shape
/// <c>BackupOrchestrator</c> is reached through for <c>IManualBackupTrigger</c>.
/// </remarks>
internal sealed class BackupTargetConnectionTester : IBackupTargetConnectionTester
{
    private readonly BackupTargetFactory _factory;

    internal BackupTargetConnectionTester(BackupTargetFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task<BackupTargetConnectionResult> TestConnectionAsync(
        CloudBackupTarget target,
        string? credentialOverride = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var backupTarget = _factory.Create(target, credentialOverride);
            return await backupTarget.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (BackupTargetException ex)
        {
            return BackupTargetConnectionResult.Failed(ex.Message, ex.Kind);
        }
    }
}
