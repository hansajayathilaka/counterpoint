using System;

namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>
/// What a successful <see cref="IGuidedRestoreService.RestoreAsync"/> did (SRS FR-11.12). A
/// failure is a thrown <see cref="ArgumentException"/> (a bad typed confirmation) or
/// <see cref="InvalidOperationException"/> (a bad backup file, wrong passphrase, or the safety
/// backup itself failing) - the same shape <c>ISettings.SaveAsync</c> already uses, so the wizard
/// can show it in plain language the same way <c>SettingsViewModel</c> does.
/// </summary>
/// <param name="SafetyBackupFilename">
/// The backup of the <em>current</em> database taken before overwriting it (FR-11.12's "back up
/// the current database before overwriting it").
/// </param>
/// <param name="RestoredSchemaVersion">The schema version the restored data was taken under.</param>
/// <param name="RestoredDataDate">The date the restored data is from.</param>
/// <param name="RequiresRestart">
/// Always true today: the restored file is staged, and takes effect the next time Counterpoint
/// starts (see <c>Counterpoint.Backup.Restore.PendingRestoreLocation</c>) - a single-connection,
/// single-instance till has no safe way to swap the live database file out from under itself
/// while it is running (CLAUDE.md "single named-mutex instance").
/// </param>
public sealed record GuidedRestoreOutcome(
    string SafetyBackupFilename,
    string RestoredSchemaVersion,
    DateTimeOffset RestoredDataDate,
    bool RequiresRestart = true);
