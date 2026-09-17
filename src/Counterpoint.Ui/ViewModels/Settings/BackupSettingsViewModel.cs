using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Security;
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
        (CloudBackupTarget.GoogleDrive, "Google Drive"),
        (CloudBackupTarget.S3Compatible, "S3-compatible storage (GCS / R2 / B2 / bucket)"),
        (CloudBackupTarget.LocalFolder, "Local folder / NAS"));

    private readonly IManualBackupTrigger? _manualBackup;
    private readonly IBackupTargetConnectionTester? _connectionTester;

    private string _dailyBackupTime = string.Empty;
    private string _retentionDays = string.Empty;
    private string _retentionCopies = string.Empty;
    private string _warnAfterDays = string.Empty;

    [ObservableProperty]
    private bool _backupOnShiftClose;

    [ObservableProperty]
    private bool _backupNowBusy;

    [ObservableProperty]
    private string _backupNowStatus = string.Empty;

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

    [ObservableProperty]
    private string _newCredential = string.Empty;

    [ObservableProperty]
    private bool _testConnectionBusy;

    [ObservableProperty]
    private string _testConnectionStatus = string.Empty;

    /// <summary>Runs the screen with no "Backup now" button - the settings screen's own tests build it this way.</summary>
    public BackupSettingsViewModel()
        : this(manualBackup: null)
    {
    }

    /// <param name="manualBackup">
    /// The owner's "Backup now" button (SRS FR-11.2, P1-T15). Null runs the screen without one -
    /// the tab still edits and saves every other field.
    /// </param>
    /// <param name="connectionTester">
    /// The off-site target's "Test connection" button (SRS FR-11.5, P4-T01). Null runs the screen
    /// without one - the target picker and credential box still edit and save.
    /// </param>
    public BackupSettingsViewModel(
        IManualBackupTrigger? manualBackup, IBackupTargetConnectionTester? connectionTester = null)
    {
        _manualBackup = manualBackup;
        _connectionTester = connectionTester;
    }

    /// <inheritdoc />
    public override string Title => "Backup";

    /// <inheritdoc />
    public override string Requirement => "FR-10.7";

    /// <summary>Whether this screen can take a backup on demand at all.</summary>
    public bool CanBackupNow => _manualBackup is not null;

    /// <summary>Raised when the owner asks for the guided restore wizard (SRS FR-11.12).</summary>
    public event EventHandler? RestoreRequested;

    /// <summary>Where the off-site copy goes.</summary>
    public IReadOnlyList<string> CloudTargetChoices => _targets.Labels;

    /// <summary>Whether this screen can test the chosen off-site target's connection at all.</summary>
    public bool CanTestConnection => _connectionTester is not null;

    /// <summary>What the credential box is asking for, depending on the chosen target.</summary>
    public string CredentialHint => _targets.Value(CloudTargetChoice) switch
    {
        CloudBackupTarget.GoogleDrive =>
            "Paste the Google Drive connection details obtained when the account was connected (refresh token).",
        CloudBackupTarget.S3Compatible =>
            "Paste the S3-compatible connection details - endpoint, region, bucket, access key and secret key.",
        CloudBackupTarget.LocalFolder =>
            "Type the full path of the local or NAS folder to copy backups into.",
        _ => "Choose an off-site copy above to enter its connection details.",
    };

    /// <summary>True when the owner has typed something to store for the chosen target's credential.</summary>
    public bool HasCredentialToStore => NewCredential.Length > 0;

    /// <summary>The off-site target currently chosen in the picker, as the enum <c>BackupSettings.CloudTarget</c> stores.</summary>
    public CloudBackupTarget SelectedCloudTarget => _targets.Value(CloudTargetChoice);

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

    /// <summary>
    /// How many days without a successful local backup the dashboard and status bar tolerate
    /// before warning (P1-T15, FR-11.7's local half).
    /// </summary>
    public string WarnAfterDays
    {
        get => _warnAfterDays;
        set => SetNumeric(ref _warnAfterDays, value);
    }

    /// <summary>Takes a backup right now (SRS FR-11.2).</summary>
    [RelayCommand]
    private async Task BackupNowAsync(CancellationToken cancellationToken)
    {
        if (_manualBackup is null || BackupNowBusy)
        {
            return;
        }

        BackupNowBusy = true;
        try
        {
            var outcome = await _manualBackup.RunNowAsync(cancellationToken).ConfigureAwait(true);

            if (!outcome.Succeeded)
            {
                BackupNowStatus = "Backup failed: " + outcome.FailureReason;
                return;
            }

            BackupNowStatus = outcome.UsbStatus switch
            {
                "OK" => "Backup " + outcome.Filename + " taken and copied to USB.",
                "FAILED" => "Backup " + outcome.Filename + " taken. USB copy did not happen: " + outcome.UsbWarning,
                _ => "Backup " + outcome.Filename + " taken.",
            };
        }
        catch (NotAuthorisedException exception)
        {
            BackupNowStatus = exception.Message;
        }
        finally
        {
            BackupNowBusy = false;
        }
    }

    /// <summary>Opens the guided restore wizard (SRS FR-11.12).</summary>
    [RelayCommand]
    private void OpenRestoreWizard() => RestoreRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Tries the chosen off-site target with whatever is typed in the credential box, or the
    /// already-stored credential when the box is empty (SRS FR-11.5, P4-T01). Never saves
    /// anything - the owner presses Save separately once satisfied.
    /// </summary>
    [RelayCommand]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (_connectionTester is null || TestConnectionBusy)
        {
            return;
        }

        var target = _targets.Value(CloudTargetChoice);
        if (target == CloudBackupTarget.None)
        {
            TestConnectionStatus = "Choose an off-site copy target first.";
            return;
        }

        TestConnectionBusy = true;
        try
        {
            var result = await _connectionTester.TestConnectionAsync(
                target,
                HasCredentialToStore ? NewCredential : null,
                cancellationToken).ConfigureAwait(true);

            TestConnectionStatus = result.Success ? result.Message : "Connection failed: " + result.Message;
        }
        finally
        {
            TestConnectionBusy = false;
        }
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

    /// <summary>Forgets the credential box, the same way <see cref="ClearPassphraseEntry"/> does.</summary>
    public void ClearCredentialEntry() => NewCredential = string.Empty;

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
        WarnAfterDays = SettingsText.FromInt(snapshot.Backup.WarnAfterDays);
        PassphraseIsSet = snapshot.Backup.PassphraseIsSet;

        ClearPassphraseEntry();
        ClearCredentialEntry();
        TestConnectionStatus = string.Empty;
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
                snapshot.Backup.PassphraseIsSet,
                SettingsText.ToInt(WarnAfterDays, snapshot.Backup.WarnAfterDays)),
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

    partial void OnNewCredentialChanged(string value) => OnPropertyChanged(nameof(HasCredentialToStore));

    partial void OnCloudTargetChoiceChanged(string value)
    {
        OnPropertyChanged(nameof(CredentialHint));
        TestConnectionStatus = string.Empty;
    }
}
