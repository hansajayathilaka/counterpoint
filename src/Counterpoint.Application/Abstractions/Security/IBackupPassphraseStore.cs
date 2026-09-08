using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Abstractions.Security;

/// <summary>
/// Keeps the owner's backup encryption passphrase (SRS FR-10.7, FR-11.4, NFR-S6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not an <c>app_setting</c> row.</b> The passphrase protects a copy of the very database
/// <c>app_setting</c> lives in, so storing it there would protect nothing. It goes to the
/// operating system's protected store, beside the SQLCipher key - Windows Credential Manager on
/// the terminal, a development file store on Linux, exactly as
/// <see cref="IDatabaseKeyStore"/> does.
/// </para>
/// <para>
/// There is no "read" that returns it to the UI. The backup writer (Phase 0 P0-T07 and Phase 4)
/// asks for it; a settings screen only ever asks <see cref="HasPassphrase"/>. Nothing logs it,
/// ever (engineering guide §7).
/// </para>
/// <para>
/// Deliberately synchronous, for the same reason <see cref="IDatabaseKeyStore"/> is: it talks to
/// a local OS store, and an async signature would buy nothing.
/// </para>
/// <para>
/// <b>Reading is open; replacing is the owner's.</b> The same split <c>ISettings</c> has, and for
/// a sharper reason: the passphrase is not an <c>app_setting</c> row, so a wrong write to it sits
/// outside the settings transaction, outside the FR-10.9 audit trail and outside anything that
/// could be rolled back or reconciled afterwards - it makes every existing backup permanently
/// unrestorable, silently. <see cref="SetPassphrase"/> therefore carries
/// <see cref="RequiresRoleAttribute"/> and nothing else here does: the backup writer reads it on
/// a schedule with nobody signed in, and a screen asks <see cref="HasPassphrase"/> as anybody
/// (SRS §3.3 ROLE-2, FR-1.6, NFR-S2, NFR-S6, AC-17, CLAUDE.md invariant 8).
/// </para>
/// <para>
/// First run is the one write that cannot pass that check - there is no owner signed in while the
/// owner account is still being created - and it does not try to: it holds the concrete
/// <see cref="BackupPassphraseStore"/> and calls the internal
/// <c>SetInitialPassphrase</c>, exactly as it holds the concrete <c>SettingsService</c> for
/// <c>SaveAsAsync</c>. Everything else in the application, the settings screen included, is handed
/// only the role-decorated interface.
/// </para>
/// </remarks>
public interface IBackupPassphraseStore
{
    /// <summary>True when a passphrase has been set. Never says what it is.</summary>
    public bool HasPassphrase();

    /// <summary>Stores the passphrase, replacing any previous one.</summary>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// The signed-in user is not an owner, or nobody is signed in. Thrown before the store is
    /// reached, so the previous passphrase is still there and every backup taken under it can
    /// still be restored (SRS NFR-S6, AC-17).
    /// </exception>
    [RequiresRole(Role.Owner)]
    public void SetPassphrase(string passphrase);

    /// <summary>
    /// Returns the stored passphrase, or null when none has been set. For the backup writer
    /// only - never for a screen, a log or an export.
    /// </summary>
    public string? TryGetPassphrase();
}
