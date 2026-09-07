using System;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Settings;

/// <summary>
/// The shop's settings, strongly typed (SRS FR-10, NFR-M1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Typed, not stringly typed.</b> A caller writes <c>settings.Financial.DecimalPlaces</c> and
/// gets an <c>int</c>; it never writes <c>Get&lt;int&gt;("financial.decimal_places")</c>. Keys
/// exist in exactly one file (<see cref="SettingKeys"/>) and are never seen outside the
/// serialiser, so a typo is a compile error rather than a shop silently trading on a default.
/// </para>
/// <para>
/// <b>Cached, and invalidated on write.</b> The eight groups come off one immutable
/// <see cref="SettingsSnapshot"/> held in memory, so reading is a field read and is safe from any
/// number of threads at once. A write publishes a new snapshot only after its transaction has
/// committed - so a change takes effect immediately, without a restart (FR-10.2, and the stated
/// risk on P1-T03), and a failed write leaves both the database and the cache exactly as they
/// were.
/// </para>
/// <para>
/// <b>Every write is audited</b> (FR-10.9), one row per changed key, with before and after JSON
/// and the acting user, inside the same transaction as the setting itself.
/// </para>
/// <para>
/// <b>Reading is open; writing is the owner's.</b> SRS §3.3 puts "settings" among the things
/// ROLE-2 may do and ROLE-1 may not, FR-1.2 makes that split a requirement and FR-1.6 counts a
/// settings change as a privileged action - so <see cref="SaveAsync"/> and
/// <see cref="UpdateAsync"/> carry <see cref="RequiresRoleAttribute"/> and nothing else here
/// does. The requirement is declared per method rather than on the interface because this
/// interface is not owner-only end to end: the sale screen reads
/// <c>Financial.DecimalPlaces</c> and the rounding rule on every line as a cashier, and
/// <c>Program.PrepareDatabaseAsync</c> calls <see cref="LoadAsync"/> at start-up before any
/// session exists. <c>RoleAuthorisation</c> forwards an unattributed member untouched, which is
/// what makes that split expressible (SRS NFR-S2, AC-17, CLAUDE.md invariant 8).
/// </para>
/// </remarks>
public interface ISettings
{
    /// <summary>FR-10.1 - shop name, address, contact and tax registration number.</summary>
    public ShopProfileSettings Shop { get; }

    /// <summary>FR-10.2 - currency, decimal places and the rounding rule.</summary>
    public FinancialSettings Financial { get; }

    /// <summary>FR-10.3 - tax-inclusive or tax-exclusive pricing, and the default tax class.</summary>
    public TaxSettings Tax { get; }

    /// <summary>FR-10.4 - the document number series.</summary>
    public NumberingSettings Numbering { get; }

    /// <summary>
    /// FR-10.5 - returns, refunds, discount ceilings and the negative-stock policy. Where
    /// <c>MaxLineDiscountRate</c> lives: the SRS files discount limits under Policy, so this
    /// framework does too.
    /// </summary>
    public PolicySettings Policy { get; }

    /// <summary>FR-10.6 - printer, paper, drawer, scanner and scale.</summary>
    public PeripheralSettings Peripherals { get; }

    /// <summary>FR-10.7 - backup schedule, destinations and retention.</summary>
    public BackupSettings Backup { get; }

    /// <summary>FR-10.8 - the receipt template.</summary>
    public ReceiptSettings Receipt { get; }

    /// <summary>All eight groups as one value, for a screen that edits several at once.</summary>
    public SettingsSnapshot Current { get; }

    /// <summary>
    /// Raised after a write has committed and the new snapshot has been published. A screen that
    /// is already open uses this to re-read; a screen that opens afterwards simply reads
    /// <see cref="Current"/>.
    /// </summary>
    /// <remarks>
    /// One event, not a message bus. There is one process, one till and one settings owner
    /// (C-01), so anything more would be machinery without a job.
    /// </remarks>
    public event EventHandler? Changed;

    /// <summary>
    /// Reads every setting from the database and publishes it. Called once at start-up, before
    /// anything reads a setting, and again by <see cref="SaveAsync"/>.
    /// </summary>
    public Task<SettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="desired"/>, writing only the settings that actually changed, and
    /// audits each one (FR-10.9).
    /// </summary>
    /// <returns>The snapshot now in force.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="LoadAsync"/> has not been called yet.
    /// </exception>
    /// <exception cref="ArgumentException">A value is outside what the shop can trade on.</exception>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// The signed-in user is not an owner, or nobody is signed in. Thrown before the service is
    /// reached, so nothing has been written (SRS §3.3 ROLE-2, FR-1.2, NFR-S2, AC-17).
    /// </exception>
    [RequiresRole(Role.Owner)]
    public Task<SettingsSnapshot> SaveAsync(
        SettingsSnapshot desired,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies an edit to the settings in force and saves the result - the convenient form of
    /// <see cref="SaveAsync"/>: <c>UpdateAsync(s =&gt; s with { Receipt = ... })</c>.
    /// </summary>
    /// <remarks>
    /// Attributed in its own right, not left to inherit the guard from the call it makes: the
    /// implementation calls <see cref="SaveAsync"/> on itself, which is an ordinary in-object call
    /// that never goes back out through the proxy. A convenience overload that is not guarded is
    /// not a convenience, it is a way round the guard.
    /// </remarks>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// The signed-in user is not an owner, or nobody is signed in.
    /// </exception>
    [RequiresRole(Role.Owner)]
    public Task<SettingsSnapshot> UpdateAsync(
        Func<SettingsSnapshot, SettingsSnapshot> edit,
        CancellationToken cancellationToken = default);
}
