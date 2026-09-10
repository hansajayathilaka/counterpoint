using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>The guided restore wizard's backend (SRS FR-11.12, FR-11.13).</summary>
/// <remarks>
/// Owner-only, and every call is written to the audit trail by the implementation - restoring the
/// till's whole database is exactly the kind of action FR-11.13 has in mind. The composition root
/// registers only the <see cref="RoleAuthorisation"/>-decorated interface; the concrete
/// implementation lives in <c>Counterpoint.Backup</c>, internal to that assembly, the same shape
/// as <c>IUserAdministration</c>.
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IGuidedRestoreService
{
    /// <summary>
    /// Reads the header and verifies the checksum of the file at <paramref name="backupFilePath"/>,
    /// without decrypting anything - no passphrase needed yet (SRS FR-11.12's "verify checksum"
    /// and "show what date the data will be restored to", in that order, before the passphrase is
    /// asked for).
    /// </summary>
    /// <exception cref="System.ArgumentException">The path is not a Counterpoint backup file.</exception>
    /// <exception cref="System.InvalidOperationException">
    /// The file is truncated, or its checksum does not match its contents - it is damaged.
    /// </exception>
    public Task<GuidedRestorePreview> PreviewAsync(string backupFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Backs up the current database, then decrypts <paramref name="request"/>'s chosen backup and
    /// stages it to take effect the next time Counterpoint starts (SRS FR-11.12).
    /// </summary>
    /// <exception cref="System.ArgumentException">
    /// <see cref="GuidedRestoreRequest.TypedConfirmation"/> does not exactly match
    /// <see cref="GuidedRestoreConfirmation.RequiredPhrase"/>. Nothing is changed.
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// The backup file is damaged, the passphrase is wrong, or the current database could not be
    /// backed up first - in every case, nothing is changed.
    /// </exception>
    public Task<GuidedRestoreOutcome> RestoreAsync(GuidedRestoreRequest request, CancellationToken cancellationToken = default);
}
