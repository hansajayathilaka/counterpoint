namespace Counterpoint.Application.Security;

/// <summary>
/// The <c>audit_log.action</c> values this task writes (SRS FR-1.6, FR-1.8, NFR-S9).
/// </summary>
/// <remarks>
/// Named constants rather than literals at the call sites, because the audit-log viewer
/// (P3-T08) filters on exactly these strings and a typo would silently hide an event from the
/// filter that was meant to find it.
/// </remarks>
public static class SecurityAuditActions
{
    /// <summary>A password verified and a session started.</summary>
    public const string LoginSucceeded = "LOGIN_SUCCEEDED";

    /// <summary>A wrong password, or a username that does not exist (NFR-S9).</summary>
    public const string LoginFailed = "LOGIN_FAILED";

    /// <summary>The attempt was not even considered: the account is locked or deactivated.</summary>
    public const string LoginRefused = "LOGIN_REFUSED";

    /// <summary>The session ended.</summary>
    public const string Logout = "LOGOUT";

    /// <summary>An owner's password verified for an override, without starting a session.</summary>
    public const string ReauthenticationSucceeded = "REAUTH_SUCCEEDED";

    /// <summary>A wrong password given for an override.</summary>
    public const string ReauthenticationFailed = "REAUTH_FAILED";

    /// <summary>An override re-authentication that was refused before the password was checked.</summary>
    public const string ReauthenticationRefused = "REAUTH_REFUSED";

    /// <summary>An owner authorised a cashier's privileged action (SRS FR-1.7).</summary>
    public const string OwnerOverrideGranted = "OWNER_OVERRIDE_GRANTED";

    /// <summary>An owner override was asked for and not granted.</summary>
    public const string OwnerOverrideRefused = "OWNER_OVERRIDE_REFUSED";

    /// <summary>A user account was created (SRS FR-1.4).</summary>
    public const string UserCreated = "USER_CREATED";

    /// <summary>A user account was deactivated (SRS FR-1.4).</summary>
    public const string UserDeactivated = "USER_DEACTIVATED";

    /// <summary>A deactivated user account was enabled again (SRS FR-1.4).</summary>
    public const string UserReactivated = "USER_REACTIVATED";

    /// <summary>An owner reset somebody's password (SRS FR-1.4).</summary>
    public const string UserPasswordReset = "USER_PASSWORD_RESET";

    /// <summary>The shop's first owner credential was set, on a database that had none.</summary>
    public const string OwnerPasswordInitialised = "OWNER_PASSWORD_INITIALISED";

    /// <summary>The <c>app_user</c> table, as <c>audit_log.entity_type</c>.</summary>
    public const string UserEntityType = "app_user";
}
