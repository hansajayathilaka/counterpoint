using System;

namespace Counterpoint.Application.Abstractions.Security;

/// <summary>
/// The base every real <see cref="IBackupPassphraseStore"/> is built on, and the seam first run
/// writes the shop's first passphrase through (SRS FR-10.7, FR-11.4, NFR-S6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this class exists at all.</b> <see cref="IBackupPassphraseStore.SetPassphrase"/> is
/// owner-only, and the composition root hands out only the role-decorated interface. First run
/// cannot satisfy that check - it is creating the owner account, so nobody is signed in yet - and
/// must not be allowed to weaken it, so it needs a way in that is not the interface. That is
/// <see cref="SetInitialPassphrase"/>: <c>internal</c> to <c>Counterpoint.Application</c>, so only
/// <c>FirstRunSetupService</c> (and the composition roots and test projects granted an
/// <c>InternalsVisibleTo</c>) can even name it. It is the same arrangement <c>SettingsService</c>
/// already uses for its internal <c>SaveAsAsync</c> beside the owner-only
/// <c>ISettings.SaveAsync</c>.
/// </para>
/// <para>
/// It lives in <c>Counterpoint.Application</c> rather than beside the concrete stores because
/// <c>FirstRunSetupService</c> lives here too, and the Application layer may not reference
/// <c>Counterpoint.Infrastructure</c> (CLAUDE.md "Project boundaries"). The platform stores
/// derive from it, are <c>internal</c> to <c>Counterpoint.Infrastructure</c>, and are reached only
/// through <c>BackupPassphraseStoreFactory</c>, so nothing can pick up an undecorated store by its
/// concrete type.
/// </para>
/// <para>
/// A derived store implements <see cref="Store"/> once; the two public ways in are this class's,
/// which is what keeps "the owner-only write and the first-run write do exactly the same thing to
/// the OS store" a structural fact rather than two implementations that might drift.
/// </para>
/// </remarks>
public abstract class BackupPassphraseStore : IBackupPassphraseStore
{
    /// <inheritdoc />
    public abstract bool HasPassphrase();

    /// <inheritdoc />
    public void SetPassphrase(string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        Store(passphrase);
    }

    /// <inheritdoc />
    public abstract string? TryGetPassphrase();

    /// <summary>
    /// First run only: stores the shop's first passphrase with no session to check, because the
    /// owner who would satisfy the check is being created in the same transaction.
    /// </summary>
    /// <remarks>
    /// Safe exactly once and exactly there: <c>IFirstRunSetup.CompleteAsync</c> is guarded by
    /// <c>IsRequiredAsync</c>, so it runs on a database that has never been configured, has no
    /// owner account and therefore has no backup for an overwritten passphrase to strand.
    /// </remarks>
    internal void SetInitialPassphrase(string passphrase)
    {
        ArgumentException.ThrowIfNullOrEmpty(passphrase);
        Store(passphrase);
    }

    /// <summary>
    /// Writes the passphrase to the platform's protected store, replacing any previous one.
    /// </summary>
    /// <param name="passphrase">Already checked to be neither null nor empty.</param>
    protected abstract void Store(string passphrase);
}
