namespace Counterpoint.Application.Settings;

/// <summary>
/// Every setting the shop has, as one immutable value (SRS FR-10.1-10.8).
/// </summary>
/// <remarks>
/// <para>
/// Immutable on purpose, and it is what makes the cache safe. Readers hold a reference to a
/// whole consistent set of settings; a write publishes a new one, atomically, after its
/// transaction has committed. Nobody ever sees half a change, and no lock is needed to read
/// (P1-T03: "cached in memory, invalidated on write").
/// </para>
/// <para>
/// Editing is a <c>with</c> expression: <c>snapshot with { Financial = ... }</c>. The settings
/// screens build the edited snapshot and hand it to <see cref="ISettings.SaveAsync"/>, which
/// writes only what actually changed.
/// </para>
/// </remarks>
/// <param name="Shop">FR-10.1.</param>
/// <param name="Financial">FR-10.2.</param>
/// <param name="Tax">FR-10.3.</param>
/// <param name="Numbering">FR-10.4.</param>
/// <param name="Policy">FR-10.5.</param>
/// <param name="Peripherals">FR-10.6.</param>
/// <param name="Backup">FR-10.7.</param>
/// <param name="Receipt">FR-10.8.</param>
public sealed record SettingsSnapshot(
    ShopProfileSettings Shop,
    FinancialSettings Financial,
    TaxSettings Tax,
    NumberingSettings Numbering,
    PolicySettings Policy,
    PeripheralSettings Peripherals,
    BackupSettings Backup,
    ReceiptSettings Receipt);
