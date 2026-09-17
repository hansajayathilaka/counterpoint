using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Abstractions.Security;

/// <summary>
/// Keeps the off-site backup target's credential - an S3 access/secret key pair, a Google Drive
/// refresh token, or a local/NAS folder path - out of <c>app_setting</c> (SRS FR-11.5, NFR-S6,
/// P4-T01).
/// </summary>
/// <remarks>
/// <para>
/// The keyed sibling of <see cref="IBackupPassphraseStore"/>, built the same way and for the same
/// reason: the operating system's protected store is where a secret Counterpoint depends on
/// belongs, not a file next to the database. Windows Credential Manager on the terminal, a
/// development file store on Linux (CLAUDE.md "Development platform note").
/// </para>
/// <para>
/// <b>Keyed, unlike the passphrase store.</b> There is exactly one backup passphrase, ever, but a
/// shop can reconfigure which off-site target it uses, and switching back should not have thrown
/// the previous target's credential away. Each <c>targetKey</c> below names one target kind (see
/// <see cref="BackupTargetCredentialKey"/>) and stores its own credential, independent of the
/// others.
/// </para>
/// <para>
/// <b>Reading is open; writing is the owner's</b>, for the same reason <c>ISettings</c> splits the
/// same way: the settings screen's "a credential is configured" indicator and the connection-test
/// button both need to read without a role, while replacing a stored credential is an owner-only
/// act with nothing to roll back if refused (SRS §3.3 ROLE-2, FR-1.6, NFR-S2, NFR-S6, AC-17,
/// CLAUDE.md invariant 8).
/// </para>
/// </remarks>
public interface IBackupTargetCredentialStore
{
    /// <summary>True when a credential is stored for <paramref name="targetKey"/>. Never says what it is.</summary>
    public bool HasCredential(string targetKey);

    /// <summary>Stores the credential for <paramref name="targetKey"/>, replacing any previous one.</summary>
    /// <exception cref="NotAuthorisedException">
    /// The signed-in user is not an owner, or nobody is signed in. Thrown before the store is
    /// reached, so the previous credential is still there (SRS NFR-S6, AC-17).
    /// </exception>
    [RequiresRole(Role.Owner)]
    public void SetCredential(string targetKey, string credential);

    /// <summary>Removes the credential stored for <paramref name="targetKey"/>, if any. A no-op when there is none.</summary>
    /// <exception cref="NotAuthorisedException">
    /// The signed-in user is not an owner, or nobody is signed in.
    /// </exception>
    [RequiresRole(Role.Owner)]
    public void RemoveCredential(string targetKey);

    /// <summary>
    /// Returns the stored credential for <paramref name="targetKey"/>, or null when none has been
    /// set. For a backup target implementation only - never for a screen, a log or an export.
    /// </summary>
    public string? TryGetCredential(string targetKey);
}
