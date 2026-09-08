using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// FR-10.7 - when a backup is taken, where it goes, how long it is kept, and whether the shop
/// has a passphrase to encrypt it with.
/// </summary>
/// <remarks>
/// <para>
/// <b>The passphrase itself is never shown, never loaded and never saved as a setting.</b>
/// <c>app_setting</c> lives inside the database the passphrase protects a copy of, so keeping it
/// there would protect nothing (NFR-S6). The screen can see one thing about it -
/// <see cref="PassphraseStatus"/>, from <c>BackupSettings.PassphraseIsSet</c> - and can offer to
/// replace it, which the window does through <c>IBackupPassphraseStore</c> and the operating
/// system's protected store.
/// </para>
/// <para>
/// The two passphrase boxes are cleared the moment they have been used, and neither is ever
/// logged (engineering guide §7).
/// </para>
/// </remarks>
public sealed partial class BackupSettingsViewModel : SettingsGroupViewModel
{
    private readonly EnumChoices<CloudBackupTarget> _targets = new(
        (CloudBackupTarget.None, "No off-site copy"),
        (CloudBackupTarget.GoogleDrive, "Google Drive"));

    private string _dailyBackupTime = string.Empty;
    private string _retentionDays = string.Empty;
    private string _retentionCopies = string.Empty;

    [ObservableProperty]
    private bool _backupOnShiftClose;

    [ObservableProperty]
    private string _localPath = string.Empty;

    [ObservableProperty]
    private string _usbPath = string.Empty;

    [ObservableProperty]
    private string _cloudTargetChoice = string.Empty;

    [ObservableProperty]
    private string _cloudAccount = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PassphraseStatus))]
    private bool _passphraseIsSet;

    [ObservableProperty]
    private string _newPassphrase = string.Empty;

    [ObservableProperty]
    private string _confirmPassphrase = string.Empty;

    /// <inheritdoc />
    public override string Title => "Backup";

    /// <inheritdoc />
    public override string Requirement => "FR-10.7";

    /// <summary>Where the off-site copy goes.</summary>
    public IReadOnlyList<string> CloudTargetChoices => _targets.Labels;

    /// <summary>All the screen may ever know about the passphrase.</summary>
    public string PassphraseStatus => PassphraseIsSet
        ? "A backup passphrase is set. Type a new one below to replace it."
        : "No backup passphrase is set yet. Backups cannot be encrypted until there is one.";

    /// <summary>Local time of the daily automatic backup, as <c>20:00</c>.</summary>
    public string DailyBackupTime
    {
        get => _dailyBackupTime;
        set => SetTime(ref _dailyBackupTime, value);
    }

    /// <summary>How many days of backups are kept before pruning.</summary>
    public string RetentionDays
    {
        get => _retentionDays;
        set => SetNumeric(ref _retentionDays, value);
    }

    /// <summary>The minimum number of copies kept regardless of age.</summary>
    public string RetentionCopies
    {
        get => _retentionCopies;
        set => SetNumeric(ref _retentionCopies, value);
    }

    /// <summary>True when the owner has typed a matching pair of passphrases to store.</summary>
    public bool HasPassphraseToStore => NewPassphrase.Length > 0
        && string.Equals(NewPassphrase, ConfirmPassphrase, StringComparison.Ordinal);

    /// <summary>Forgets both passphrase boxes.</summary>
    public void ClearPassphraseEntry()
    {
        NewPassphrase = string.Empty;
        ConfirmPassphrase = string.Empty;
    }

    /// <inheritdoc />
    public override void Load(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        DailyBackupTime = SettingsText.FromTime(snapshot.Backup.DailyBackupTime);
        BackupOnShiftClose = snapshot.Backup.BackupOnShiftClose;
        LocalPath = snapshot.Backup.LocalPath;
        UsbPath = snapshot.Backup.UsbPath;
        CloudTargetChoice = _targets.Label(snapshot.Backup.CloudTarget);
        CloudAccount = snapshot.Backup.CloudAccount;
        RetentionDays = SettingsText.FromInt(snapshot.Backup.RetentionDays);
        RetentionCopies = SettingsText.FromInt(snapshot.Backup.RetentionCopies);
        PassphraseIsSet = snapshot.Backup.PassphraseIsSet;

        ClearPassphraseEntry();
    }

    /// <inheritdoc />
    public override SettingsSnapshot Apply(SettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot with
        {
            Backup = new BackupSettings(
                SettingsText.TryToTime(DailyBackupTime, out var time)
                    ? time
                    : snapshot.Backup.DailyBackupTime,
                BackupOnShiftClose,
                LocalPath.Trim(),
                UsbPath.Trim(),
                _targets.Value(CloudTargetChoice),
                CloudAccount.Trim(),
                SettingsText.ToInt(RetentionDays, snapshot.Backup.RetentionDays),
                SettingsText.ToInt(RetentionCopies, snapshot.Backup.RetentionCopies),

                // Never taken from the screen. SaveAsync reads it back from the protected store.
                snapshot.Backup.PassphraseIsSet),
        };
    }

    /// <inheritdoc />
    public override string? Validate()
    {
        if (DailyBackupTime.Length > 0 && !SettingsText.TryToTime(DailyBackupTime, out _))
        {
            return "The daily backup time must be a time of day, written as 20:00.";
        }

        if (NewPassphrase.Length > 0
            && !string.Equals(NewPassphrase, ConfirmPassphrase, StringComparison.Ordinal))
        {
            return "The two backup passphrases are not the same. Type them again.";
        }

        return null;
    }

    partial void OnNewPassphraseChanged(string value) => OnPropertyChanged(nameof(HasPassphraseToStore));

    partial void OnConfirmPassphraseChanged(string value) => OnPropertyChanged(nameof(HasPassphraseToStore));
}
