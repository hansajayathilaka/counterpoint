using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// Creating, deactivating and resetting accounts. Owner only (SRS FR-1.4, NFR-S2, AC-17).
/// </summary>
/// <remarks>
/// <para>
/// The requirement is declared on the interface, so it applies to every method here and to every
/// method added here later. <see cref="RoleAuthorisation"/> enforces it in front of the
/// implementation, in this layer - a cashier calling any of these gets a
/// <see cref="NotAuthorisedException"/> whether they came through the user-management screen,
/// through some other screen, or through no screen at all. That last case is AC-17.
/// </para>
/// <para>
/// Listing is owner-only too, not just changing. Who the shop's accounts are, when they last
/// signed in and which of them are locked is the owner's business.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IUserAdministration
{
    /// <summary>Every account, active and not.</summary>
    public Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Creates an account and returns its id.</summary>
    public Task<long> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns an account off. History keeps it: an account that has rung up a bill is never
    /// deleted, only deactivated (the FR-2.1 pattern, applied to users).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// It is the last enabled owner account. SRS FR-1.4 requires the shop to keep one.
    /// </exception>
    public Task DeactivateAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>Turns a deactivated account back on.</summary>
    public Task ReactivateAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a new password. This is the only way back from a forgotten password or a locked
    /// account - there is nothing to recover, so there is something to replace.
    /// </summary>
    public Task ResetPasswordAsync(long userId, string newPassword, CancellationToken cancellationToken = default);
}
