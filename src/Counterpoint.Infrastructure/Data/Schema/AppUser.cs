namespace Counterpoint.Infrastructure.Data.Schema;

/// <summary>Row of <c>app_user</c>. See Schema/README.md.</summary>
internal sealed class AppUser
{
    public long Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Argon2id encoded string.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public bool Active { get; set; }

    /// <summary>Plain count. Not scaled.</summary>
    public int FailedAttempts { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? LastLogin { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
