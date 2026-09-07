namespace Counterpoint.Application.Security;

/// <summary>What happened when somebody tried to sign in (SRS FR-1.1, NFR-S9).</summary>
public enum LoginOutcome
{
    /// <summary>The password verified. This is the only value that grants anything.</summary>
    Succeeded = 0,

    /// <summary>
    /// No such user, or the wrong password. One value for both on purpose: telling them apart
    /// tells somebody at the counter which usernames exist.
    /// </summary>
    InvalidCredentials = 1,

    /// <summary>Too many consecutive failures. See <see cref="AccountLockout"/>.</summary>
    LockedOut = 2,

    /// <summary>The account exists but the owner has deactivated it (SRS FR-1.4).</summary>
    Deactivated = 3,
}
