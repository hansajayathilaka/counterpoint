using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>The owner's "Backup now" button (SRS FR-11.2).</summary>
/// <remarks>
/// The composition root registers only the <see cref="RoleAuthorisation"/>-decorated interface;
/// the concrete implementation is internal to <c>Counterpoint.Backup</c>, so nothing can reach it
/// without the role check (SRS NFR-S2, AC-17), the same shape as <c>IUserAdministration</c>.
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IManualBackupTrigger
{
    /// <summary>Takes a backup right now, exactly as <see cref="IBackupOrchestrator.RunAsync"/> would.</summary>
    public Task<BackupOutcome> RunNowAsync(CancellationToken cancellationToken = default);
}
