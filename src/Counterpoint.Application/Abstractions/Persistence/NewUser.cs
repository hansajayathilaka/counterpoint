using System;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>An <c>app_user</c> row to insert (SRS FR-1.4).</summary>
/// <param name="Username">Unique. The store relies on <c>ux_app_user_username</c> to keep it so.</param>
/// <param name="DisplayName">What the screen calls them.</param>
/// <param name="PasswordHash">An Argon2id encoded string. Never a password.</param>
/// <param name="Role">Cashier or owner.</param>
/// <param name="CreatedAt">From the injected clock, never <c>DateTimeOffset.Now</c>.</param>
public sealed record NewUser(
    string Username,
    string DisplayName,
    string PasswordHash,
    Role Role,
    DateTimeOffset CreatedAt);
