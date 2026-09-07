using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads and writes <c>app_user</c> (SRS FR-1.1, FR-1.4, NFR-S9).
/// </summary>
/// <remarks>
/// <para>
/// A port: the Application layer says what it needs, <c>Counterpoint.Infrastructure</c> supplies
/// the SQLite implementation. <c>app_user</c> is not one of the append-only tables
/// (CLAUDE.md invariant 5), so the counters, the lock and the hash are ordinary updates - but
/// every one of them is paired with an <c>audit_log</c> row by the caller, inside the same
/// transaction.
/// </para>
/// <para>
/// The write methods take the values to store rather than a whole record, so a caller cannot
/// round-trip a stale row and quietly undo somebody else's change to a column it was not
/// interested in.
/// </para>
/// </remarks>
public interface IUserStore
{
    /// <summary>The user with this username, or null. Case-sensitive, as the unique index is.</summary>
    public Task<UserRecord?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>The user with this id, or null.</summary>
    public Task<UserRecord?> FindByIdAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every active owner account. Used to enforce "at least one enabled owner account, always"
    /// (SRS FR-1.4), and to work out whether the shop has a usable credential yet at all.
    /// </summary>
    public Task<IReadOnlyList<UserRecord>> ListActiveOwnersAsync(CancellationToken cancellationToken = default);

    /// <summary>Every user, for the owner's user-management screen. No password hashes.</summary>
    public Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts a user and returns the new id. Joins the caller's transaction.</summary>
    public Task<long> CreateAsync(NewUser user, CancellationToken cancellationToken = default);

    /// <summary>Replaces the stored hash. Joins the caller's transaction.</summary>
    public Task SetPasswordHashAsync(long userId, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>Activates or deactivates the account. Joins the caller's transaction.</summary>
    public Task SetActiveAsync(long userId, bool active, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a correct password: clears the failure counter and the lock, and stamps
    /// <c>last_login</c>. Joins the caller's transaction.
    /// </summary>
    public Task RecordSuccessfulSignInAsync(long userId, DateTimeOffset at, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the failure counter and the lock without touching <c>last_login</c> - a correct
    /// password given for an owner override is not a sign-in. Joins the caller's transaction.
    /// </summary>
    public Task ClearFailedAttemptsAsync(long userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a wrong password: the new consecutive-failure count and the lock it earned, if
    /// any. Joins the caller's transaction.
    /// </summary>
    public Task RecordFailedSignInAsync(
        long userId,
        int failedAttempts,
        DateTimeOffset? lockedUntil,
        CancellationToken cancellationToken = default);
}
