using System;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// A user as the owner's user-management screen sees them (SRS FR-1.4).
/// </summary>
/// <param name="Id">The <c>app_user.id</c>.</param>
/// <param name="Username">Unique, as typed at sign-in.</param>
/// <param name="DisplayName">What the screen calls them.</param>
/// <param name="Role">Cashier or owner.</param>
/// <param name="Active">False once deactivated.</param>
/// <param name="LockedUntil">When the current lock lifts, or null when not locked.</param>
/// <param name="LastLogin">The last successful sign-in, or null if never.</param>
/// <remarks>
/// There is no password hash on this projection, the same way a cashier's catalogue projection
/// has no cost field: the safest way to keep something off a screen is for the shape the screen
/// is handed not to have it (CLAUDE.md invariant 8).
/// </remarks>
public sealed record UserSummary(
    long Id,
    string Username,
    string DisplayName,
    Role Role,
    bool Active,
    DateTimeOffset? LockedUntil,
    DateTimeOffset? LastLogin);
