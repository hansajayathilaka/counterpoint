using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// Sign-in, failed-attempt counting, lockout and the audit trail behind all three
/// (SRS FR-1.1, FR-1.6, FR-1.8, NFR-S1, NFR-S9).
/// </summary>
/// <remarks>
/// <para>
/// <b>The shape of one attempt.</b> Read the user on a read connection, run Argon2id outside any
/// transaction, then open exactly one write transaction for "what this attempt changed and the
/// audit row that says so". Verification is a tenth of a second of pure computation; holding the
/// single writer for it would put a bill behind somebody's typo (CLAUDE.md invariant 7 in
/// spirit - nothing blocks the sale that does not have to).
/// </para>
/// <para>
/// <b>Every attempt is logged, including the ones that never reach the password.</b> NFR-S9 asks
/// for failed attempts to be logged and rate-limited; an attempt refused because the account was
/// already locked is exactly the attempt an investigation wants to see, so it is written too,
/// under <see cref="SecurityAuditActions.LoginRefused"/>.
/// </para>
/// <para>
/// <b>The counter is consecutive.</b> A correct password clears it and the lock. Attempts made
/// while locked do not increment it - see <see cref="AccountLockout"/> for why.
/// </para>
/// </remarks>
public sealed class AuthenticationService : IAuthenticationService
{
    private readonly IUserStore _users;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditTrail _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITillSessionProvider _shifts;
    private readonly Session _session;
    private readonly TimeProvider _timeProvider;

    public AuthenticationService(
        IUserStore users,
        IPasswordHasher hasher,
        IAuditTrail audit,
        IUnitOfWork unitOfWork,
        ITillSessionProvider shifts,
        Session session,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(hasher);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(shifts);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _users = users;
        _hasher = hasher;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _shifts = shifts;
        _session = session;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<LoginResult> LogInAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await AuthenticateAsync(username, password, signingIn: true, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return result;
        }

        // C-01 permits exactly one open shift, so "the shift this session is trading in" is
        // whichever one is open. Opening and closing one is P1-T14 and P3-T01.
        var till = await _shifts.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        _session.SignIn(result.User!, till?.ShiftId);

        return result;
    }

    /// <inheritdoc />
    public Task<LoginResult> ReauthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default) =>
        AuthenticateAsync(username, password, signingIn: false, cancellationToken);

    /// <inheritdoc />
    public async Task LogOutAsync(CancellationToken cancellationToken = default)
    {
        var user = _session.CurrentUser;
        if (user is null)
        {
            return;
        }

        var now = _timeProvider.GetLocalNow();

        await _unitOfWork.ExecuteInTransactionAsync(
            token => _audit.RecordAsync(
                new AuditEntry(
                    now,
                    user.Id,
                    SecurityAuditActions.Logout,
                    SecurityAuditActions.UserEntityType,
                    user.Id,
                    AfterJson: SecurityAuditJson.Object(("username", user.Username))),
                token),
            cancellationToken).ConfigureAwait(false);

        _session.SignOut();
    }

    /// <summary>
    /// One attempt, from the username to the audit row.
    /// </summary>
    /// <param name="signingIn">
    /// True for a sign-in, false for the re-authentication behind an owner override. It picks the
    /// audit actions and decides whether <c>last_login</c> moves - proving who you are in order
    /// to approve a discount is not a sign-in, and recording it as one would misreport who was on
    /// the till.
    /// </param>
    private async Task<LoginResult> AuthenticateAsync(
        string username,
        string password,
        bool signingIn,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);

        var now = _timeProvider.GetLocalNow();
        var user = await _users.FindByUsernameAsync(username.Trim(), cancellationToken).ConfigureAwait(false);

        if (user is null)
        {
            // Logged with a null user id: something was attempted, and no row in app_user owns
            // it. The username is recorded because that is the evidence; nothing else is.
            await RecordAsync(
                signingIn ? SecurityAuditActions.LoginFailed : SecurityAuditActions.ReauthenticationFailed,
                userId: null,
                entityId: null,
                now,
                SecurityAuditJson.Object(
                    ("username", username.Trim()),
                    ("reason", "NO_SUCH_USER")),
                cancellationToken).ConfigureAwait(false);

            return InvalidCredentials();
        }

        if (!user.Active)
        {
            await RecordRefusalAsync(signingIn, user, now, "DEACTIVATED", cancellationToken).ConfigureAwait(false);

            return new LoginResult(
                LoginOutcome.Deactivated,
                "That account has been turned off. The owner can switch it back on.");
        }

        if (user.LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            await RecordRefusalAsync(signingIn, user, now, "LOCKED", cancellationToken).ConfigureAwait(false);

            return LockedOut(lockedUntil, now);
        }

        // Outside the transaction, on purpose. See the class remarks.
        if (!_hasher.Verify(password, user.PasswordHash))
        {
            return await RecordFailureAsync(signingIn, user, now, cancellationToken).ConfigureAwait(false);
        }

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (signingIn)
                {
                    await _users.RecordSuccessfulSignInAsync(user.Id, now, token).ConfigureAwait(false);
                }
                else
                {
                    await _users.ClearFailedAttemptsAsync(user.Id, token).ConfigureAwait(false);
                }

                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        user.Id,
                        signingIn
                            ? SecurityAuditActions.LoginSucceeded
                            : SecurityAuditActions.ReauthenticationSucceeded,
                        SecurityAuditActions.UserEntityType,
                        user.Id,
                        AfterJson: SecurityAuditJson.Object(
                            ("username", user.Username),
                            ("role", Roles.ToToken(user.Role)))),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return new LoginResult(
            LoginOutcome.Succeeded,
            "Signed in.",
            new AuthenticatedUser(user.Id, user.Username, user.DisplayName, user.Role));
    }

    /// <summary>
    /// Counts the failure, applies the backoff it earned, and records both - all in one
    /// transaction, so an account can never be locked without a row saying why.
    /// </summary>
    private async Task<LoginResult> RecordFailureAsync(
        bool signingIn,
        UserRecord user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var failures = user.FailedAttempts + 1;
        var duration = AccountLockout.DurationFor(failures);
        var lockedUntil = duration is { } window ? now + window : (DateTimeOffset?)null;

        await _unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                await _users.RecordFailedSignInAsync(user.Id, failures, lockedUntil, token).ConfigureAwait(false);

                await _audit.RecordAsync(
                    new AuditEntry(
                        now,
                        user.Id,
                        signingIn
                            ? SecurityAuditActions.LoginFailed
                            : SecurityAuditActions.ReauthenticationFailed,
                        SecurityAuditActions.UserEntityType,
                        user.Id,
                        AfterJson: SecurityAuditJson.Object(
                            ("username", user.Username),
                            ("reason", "WRONG_PASSWORD"),
                            ("failed_attempts", failures),
                            ("locked", lockedUntil is not null))),
                    token).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

        return lockedUntil is { } until ? LockedOut(until, now) : InvalidCredentials();
    }

    private Task RecordRefusalAsync(
        bool signingIn,
        UserRecord user,
        DateTimeOffset now,
        string reason,
        CancellationToken cancellationToken) =>
        RecordAsync(
            signingIn ? SecurityAuditActions.LoginRefused : SecurityAuditActions.ReauthenticationRefused,
            user.Id,
            user.Id,
            now,
            SecurityAuditJson.Object(
                ("username", user.Username),
                ("reason", reason)),
            cancellationToken);

    private Task RecordAsync(
        string action,
        long? userId,
        long? entityId,
        DateTimeOffset now,
        string afterJson,
        CancellationToken cancellationToken) =>
        _unitOfWork.ExecuteInTransactionAsync(
            token => _audit.RecordAsync(
                new AuditEntry(
                    now,
                    userId,
                    action,
                    SecurityAuditActions.UserEntityType,
                    entityId,
                    AfterJson: afterJson),
                token),
            cancellationToken);

    /// <summary>
    /// One message for "no such user" and for "wrong password". Telling them apart would let
    /// anybody standing at the counter enumerate the shop's usernames.
    /// </summary>
    private static LoginResult InvalidCredentials() => new(
        LoginOutcome.InvalidCredentials,
        "That username and password do not match. Check the caps lock and try again.");

    /// <summary>
    /// Says how long is left, rounded up to the next whole minute, because "try again in 0
    /// minutes" is not an instruction (SRS UI-06).
    /// </summary>
    /// <remarks>
    /// The rounding is integer arithmetic on ticks rather than <c>Math.Ceiling</c> on
    /// <c>TotalMinutes</c>: binary floating point is banned throughout this layer
    /// (CLAUDE.md invariant 1), and a rule with an exception in it is not a rule.
    /// </remarks>
    private static LoginResult LockedOut(DateTimeOffset lockedUntil, DateTimeOffset now)
    {
        var remaining = lockedUntil - now;
        var minutes = (remaining.Ticks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute;

        var message = minutes <= 1
            ? "That account is locked after too many wrong passwords. Try again in about a minute."
            : string.Create(
                CultureInfo.CurrentCulture,
                $"That account is locked after too many wrong passwords. Try again in {minutes} minutes, or ask the owner to reset the password.");

        return new LoginResult(LoginOutcome.LockedOut, message, LockedUntil: lockedUntil);
    }
}
