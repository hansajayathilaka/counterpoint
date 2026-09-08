using System.Collections.Generic;

namespace Counterpoint.Application.Settings.FirstRun;

/// <summary>
/// Everything the first-run wizard collects before an empty database becomes a shop that can
/// trade (P1-T03: shop profile, currency and decimals, tax classes, bill number format, owner
/// account, backup folder and passphrase).
/// </summary>
/// <remarks>
/// One value, so the wizard can build it screen by screen and the setup runs once, atomically,
/// at the end - rather than committing a half-configured shop if the owner closes the window on
/// page four.
/// </remarks>
/// <param name="Settings">
/// The eight FR-10 groups as the wizard leaves them. Start from
/// <see cref="SettingDefaults.Snapshot"/> and change what the owner said.
/// </param>
/// <param name="TaxClasses">
/// The classes to create. Empty means just the one named by
/// <c>Settings.Tax.DefaultTaxClassName</c>, at <c>Settings.Tax.DefaultTaxRate</c>.
/// </param>
/// <param name="OwnerUsername">
/// The username of the owner account whose first password is being set. The account itself
/// already exists, with a hash nothing can authenticate against, so this names it rather than
/// creating it (docs/01_DATA_MODEL.md §11).
/// </param>
/// <param name="OwnerPassword">The owner's chosen password. Never logged, never audited.</param>
/// <param name="BackupPassphrase">
/// The passphrase the shop's backups are encrypted with (FR-11.4), or null to leave it unset for
/// now. It goes to the operating system's protected store, never into the database.
/// </param>
public sealed record FirstRunSetupRequest(
    SettingsSnapshot Settings,
    IReadOnlyList<TaxClassDefinition> TaxClasses,
    string OwnerUsername,
    string OwnerPassword,
    string? BackupPassphrase);
