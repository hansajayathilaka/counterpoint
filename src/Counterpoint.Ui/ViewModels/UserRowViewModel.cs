using System;
using System.Globalization;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.Security;

namespace Counterpoint.Ui.ViewModels;

/// <summary>One row of the user-management list (SRS FR-1.4).</summary>
/// <remarks>
/// Built from a <see cref="UserSummary"/>, which has no password hash on it. There is nothing
/// here for a screen to leak.
/// </remarks>
public sealed class UserRowViewModel
{
    public UserRowViewModel(UserSummary user)
    {
        ArgumentNullException.ThrowIfNull(user);

        Id = user.Id;
        Username = user.Username;
        DisplayName = user.DisplayName;
        RoleText = user.Role == Role.Owner ? "Owner" : "Cashier";
        Active = user.Active;
        StateText = user.Active ? "active" : "off";

        LastLoginText = user.LastLogin is { } last
            ? last.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)
            : "never signed in";

        LockedText = user.LockedUntil is { } until
            ? "locked until " + until.ToString("HH:mm", CultureInfo.CurrentCulture)
            : string.Empty;
    }

    public long Id { get; }

    public string Username { get; }

    public string DisplayName { get; }

    public string RoleText { get; }

    public bool Active { get; }

    public string StateText { get; }

    public string LastLoginText { get; }

    public string LockedText { get; }
}
