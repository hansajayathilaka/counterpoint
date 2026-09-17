namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// Why a call to an off-site backup target failed, distinct enough that the screen and the
/// upload worker can each say something more useful than "upload failed" (SRS FR-11.5, P4-T01,
/// P4-T03's own UI-06 failure-category requirement).
/// </summary>
public enum BackupTargetFailureKind
{
    /// <summary>
    /// The stored credential was rejected outright - a wrong key, a revoked token that is not
    /// specifically an expired OAuth grant, or a bucket/account that does not exist.
    /// </summary>
    CredentialRejected,

    /// <summary>
    /// An OAuth refresh token has expired or been revoked (Google Drive). Distinct from
    /// <see cref="CredentialRejected"/> on purpose - the fix is "reconnect the account", not
    /// "check what you typed" (P4-T01's own stated risk).
    /// </summary>
    AuthorisationExpired,

    /// <summary>No internet, DNS failure, or the endpoint could not be reached at all.</summary>
    NetworkUnavailable,

    /// <summary>
    /// The stored credential is missing or cannot be parsed - nothing was ever configured for
    /// this target, or what is there is not valid for it.
    /// </summary>
    ConfigurationError,

    /// <summary>Anything else - reported with as much of the underlying message as is safe to show.</summary>
    Other,
}
