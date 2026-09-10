using System;

namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.7 - when a backup is taken, where it goes and how long it is kept.
/// </summary>
/// <remarks>
/// <b>The encryption passphrase is not here, and never will be.</b> <c>app_setting</c> is plain
/// text inside the database the passphrase protects a copy of, so keeping it there would defeat
/// the point. It goes to the operating system's protected store through
/// <c>IBackupPassphraseStore</c>; <see cref="PassphraseIsSet"/> is the only thing the settings
/// screen ever sees of it (engineering guide §7 "never log").
/// </remarks>
/// <param name="DailyBackupTime">Local time of the daily automatic backup (FR-11.1).</param>
/// <param name="BackupOnShiftClose">Whether closing a shift also takes one (FR-11.1).</param>
/// <param name="LocalPath">
/// Folder the backup is written to first. Empty means the <c>backups</c> folder inside the data
/// directory, which is what the installer creates.
/// </param>
/// <param name="UsbPath">
/// Folder on the attached USB drive. Empty means no USB copy (FR-11.3).
/// </param>
/// <param name="CloudTarget">The off-site target (Q-D).</param>
/// <param name="CloudAccount">
/// The account the off-site copy is uploaded as. A name, never a credential.
/// </param>
/// <param name="RetentionDays">How many days of backups are kept before pruning (P4-T04).</param>
/// <param name="RetentionCopies">The minimum number of copies kept regardless of age.</param>
/// <param name="PassphraseIsSet">
/// Whether a backup passphrase exists in the protected store. Read-only from the settings
/// screen's point of view: setting it goes through the first-run wizard or
/// <c>IBackupPassphraseStore</c>, never through a settings row.
/// </param>
/// <param name="WarnAfterDays">
/// How many days without a successful local backup the dashboard and status bar tolerate before
/// they start warning (P1-T15, FR-11.7's local half - the cloud figure FR-11.7 also names is
/// Phase 4's). Trailing, with a default, so a caller built against the eight-argument constructor
/// this record used to have still compiles.
/// </param>
public sealed record BackupSettings(
    TimeOnly DailyBackupTime,
    bool BackupOnShiftClose,
    string LocalPath,
    string UsbPath,
    CloudBackupTarget CloudTarget,
    string CloudAccount,
    int RetentionDays,
    int RetentionCopies,
    bool PassphraseIsSet,
    int WarnAfterDays = 2);
