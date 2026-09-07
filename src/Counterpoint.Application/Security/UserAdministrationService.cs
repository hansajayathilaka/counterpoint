using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// The owner's user management (SRS FR-1.4, FR-1.6).
/// </summary>
/// <remarks>
/// <para>
/// It contains no role check of its own, on purpose. The requirement is declared on
/// <see cref="IUserAdministration"/> and enforced by <see cref="RoleAuthorisation"/> in front of
/// this class, so there is exactly one place the check happens and exactly one place it can be
/// got wrong - rather than five copies of an <c>if</c> that a sixth method will forget.
/// </para>
/// <para>
/// <b>Internal, and that is a security control rather than a style choice.</b> A public class
/// with a public constructor and six independently registered dependencies can be resolved from
/// the container, or simply <c>new</c>ed up, without <see cref="RoleAuthorisation"/> anywhere
/// near it - and the compiler would not say a word. Being invisible outside this assembly means
/// the only <see cref="IUserAdministration"/> anything can obtain is the decorated one the
/// composition root builds. <c>ArchitectureTests.ConcreteOwnerOnlyApplicationServicesAreNotPublic</c>
/// keeps that true as services multiply.
/// </para>
/// <para>
/// Every change is one transaction carrying both the row and the <c>audit_log</c> entry that
/// says who changed it (SRS FR-1.6). A password reset that committed without its audit row
/// would be exactly the change an audit exists to catch.
/// </para>
/// </remarks>
internal sealed class UserAdministrationService : IUserAdministration
{
    private readonly IUserStore _users;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISession _session;
    private readonly TimeProvider _timeProvider;

    public UserAdministrationService(
        IUserStore users,
        IPasswordHasher hasher,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        ISession session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _users = users;
        _hasher = hasher;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        _users.ListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<long> CreateAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var username = command.Username.Trim();
        var displayName = command.DisplayName.Trim();

        if (username.Length == 0)
        {
            throw new InvalidOperationException("A user needs a username to sign in with.");
        }

        if (displayName.Length == 0)
        {
            throw new InvalidOperationException("A user needs a name to show on the screen.");
        }

        PasswordHasher.RequirePasswordIsAcceptable(command.Password);

        if (await _users.FindByUsernameAsync(username, cancellationToken).ConfigureAwait(false) is not null)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.CurrentCulture,
                $"There is already a user called '{username}'. Pick another username."));
        }

        // Hashed before the transaction opens: it is a tenth of a second of computation and the
        // write gate should be held for the write and nothing else.
        var passwordHash = _hasher.Hash(command.Password);
        var now = _timeProvider.GetLocalNow();

        return await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var userId = await _users
                    .CreateAsync(new NewUser(username, displayName, passwordHash, command.Role, now), token)
                    .ConfigureAwait(false);

                await RecordAsync(
                    SecurityAuditActions.UserCreated,
                    userId,
                    now,
                    before: null,
                    after: SecurityAuditJson.Object(
                        ("username", username),
                        ("display_name", displayName),
                        ("role", Roles.ToToken(command.Role)),
                        ("active", true)),
                    reason: null,
                    token).ConfigureAwait(false);

                return userId;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeactivateAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);

        if (!user.Active)
        {
            return;
        }

        // FR-1.4: the shop must always keep one enabled owner. Checked here, in the Application
        // layer, rather than by hiding a button - a till with no way in is not a state the UI
        // gets to be the only thing standing between the shop and.
        if (user.Role == Role.Owner)
        {
            var owners = await _users.ListActiveOwnersAsync(cancellationToken).ConfigureAwait(false);

            if (!owners.Any(other => other.Id != userId))
            {
                throw new InvalidOperationException(
                    "This is the only owner account left. Create another owner before turning this one off, "
                    + "or nobody will be able to manage the shop.");
            }
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _users.SetActiveAsync(userId, active: false, token).ConfigureAwait(false);

                await RecordAsync(
                    SecurityAuditActions.UserDeactivated,
                    userId,
                    now,
                    before: SecurityAuditJson.Object(("username", user.Username), ("active", true)),
                    after: SecurityAuditJson.Object(("username", user.Username), ("active", false)),
                    reason: null,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReactivateAsync(long userId, CancellationToken cancellationToken = default)
    {
        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);

        if (user.Active)
        {
            return;
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _users.SetActiveAsync(userId, active: true, token).ConfigureAwait(false);

                await RecordAsync(
                    SecurityAuditActions.UserReactivated,
                    userId,
                    now,
                    before: SecurityAuditJson.Object(("username", user.Username), ("active", false)),
                    after: SecurityAuditJson.Object(("username", user.Username), ("active", true)),
                    reason: null,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetPasswordAsync(
        long userId,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newPassword);

        var user = await RequireUserAsync(userId, cancellationToken).ConfigureAwait(false);

        PasswordHasher.RequirePasswordIsAcceptable(newPassword);

        var passwordHash = _hasher.Hash(newPassword);
        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _users.SetPasswordHashAsync(userId, passwordHash, token).ConfigureAwait(false);

                // A reset is also the way out of a lockout: the owner is standing there, and
                // making the shop wait out the backoff as well would be a punishment with no
                // remaining purpose (SRS NFR-S9, FR-1.4).
                await _users.ClearFailedAttemptsAsync(userId, token).ConfigureAwait(false);

                // No hash in the payload, old or new. An audit row records that the password
                // changed and who changed it - never any part of the credential itself.
                await RecordAsync(
                    SecurityAuditActions.UserPasswordReset,
                    userId,
                    now,
                    before: null,
                    after: SecurityAuditJson.Object(("username", user.Username)),
                    reason: null,
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<UserRecord> RequireUserAsync(long userId, CancellationToken cancellationToken) =>
        await _users.FindByIdAsync(userId, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException(string.Create(
            CultureInfo.CurrentCulture,
            $"There is no user with id {userId}. It may have been removed since this screen was opened."));

    /// <summary>
    /// Writes the audit row against the owner who is signed in. The role decorator has already
    /// guaranteed there is one, so a missing session here would be a wiring bug rather than a
    /// permissions one, and it says so.
    /// </summary>
    private Task RecordAsync(
        string action,
        long userId,
        DateTimeOffset now,
        string? before,
        string after,
        string? reason,
        CancellationToken cancellationToken)
    {
        var actor = _session.CurrentUser ?? throw new InvalidOperationException(
            "User administration ran without a session. The role decorator should have refused this call; "
            + "the service is registered without it.");

        return _audit.RecordAsync(
            new AuditEntry(
                now,
                actor.Id,
                action,
                SecurityAuditActions.UserEntityType,
                userId,
                before,
                after,
                reason),
            cancellationToken);
    }
}
