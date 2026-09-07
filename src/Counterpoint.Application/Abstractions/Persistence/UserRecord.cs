using System;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One <c>app_user</c> row, as the authentication decision needs it (SRS FR-1.1, NFR-S9).
/// </summary>
/// <param name="Id">The <c>app_user.id</c>.</param>
/// <param name="Username">Unique, as typed at sign-in.</param>
/// <param name="DisplayName">What the screen calls them.</param>
/// <param name="PasswordHash">
/// The Argon2id encoded string, or the unusable placeholder a freshly seeded account carries.
/// Never a password: there is nothing here to reverse.
/// </param>
/// <param name="Role">Cashier or owner (SRS §3.3).</param>
/// <param name="Active">False once the owner has deactivated the account (SRS FR-1.4).</param>
/// <param name="FailedAttempts">Consecutive failures since the last correct password.</param>
/// <param name="LockedUntil">When the current lock lifts, or null when not locked.</param>
/// <param name="LastLogin">The last successful sign-in.</param>
/// <remarks>
/// This is deliberately the only shape that carries <paramref name="PasswordHash"/>, and it never
/// leaves the Application layer: what a screen gets is a <see cref="UserSummary"/>.
/// </remarks>
public sealed record UserRecord(
    long Id,
    string Username,
    string DisplayName,
    string PasswordHash,
    Role Role,
    bool Active,
    int FailedAttempts,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastLogin);
