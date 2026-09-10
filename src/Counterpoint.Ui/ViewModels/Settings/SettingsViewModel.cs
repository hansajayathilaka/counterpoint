using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// The owner's settings screen: the eight FR-10 groups, edited together and saved as one
/// (SRS FR-10.1-10.9, NFR-M1).
/// </summary>
/// <remarks>
/// <para>
/// <b>It re-reads on open.</b> <see cref="LoadCommand"/> runs every time the window is shown and
/// fills the eight groups from <see cref="ISettings.Current"/>, and the screen also listens for
/// <see cref="ISettings.Changed"/> so an edit made elsewhere does not leave it showing yesterday's
/// answer. That is P1-T03's stated risk - "settings read at start-up and cached forever" - closed
/// at both ends.
/// </para>
/// <para>
/// <b>It saves the whole snapshot, and decides nothing.</b> The eight groups build one
/// <see cref="SettingsSnapshot"/> and it goes to <see cref="ISettings.SaveAsync"/>, which
/// validates it, writes only the keys that actually changed, audits each one with before and
/// after JSON (FR-10.9) and republishes the cache. Every refusal comes back as a sentence, and
/// the screen shows it rather than interpreting it (SRS UI-06, CLAUDE.md invariant 8).
/// </para>
/// <para>
/// <b>The backup passphrase is not a setting.</b> When the owner has typed a new one it goes to
/// the operating system's protected store through <see cref="IBackupPassphraseStore"/> before the
/// snapshot is saved, in that order for the reason <c>FirstRunSetupService</c> gives: a
/// passphrase stored with nothing encrypted under it is inert, but a shop that believes it has
/// one it does not have cannot restore a backup (NFR-S6, FR-11.4).
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ISettings _settings;
    private readonly IBackupPassphraseStore _passphrases;
    private readonly Action<Action> _onUiThread;

    /// <summary>The settings the boxes were last filled from. What "changed" is measured against.</summary>
    private SettingsSnapshot _loaded = SettingDefaults.Snapshot;

    private bool _discardConfirmed;

    [ObservableProperty]
    private string _status = "Loading settings...";

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private bool _hasUnsavedChanges;

    public SettingsViewModel(ISettings settings, IBackupPassphraseStore passphrases)
        : this(settings, passphrases, run => run(), preview: null, manualBackup: null)
    {
    }

    /// <param name="onUiThread">
    /// How to get back onto the thread the screen lives on. <see cref="ISettings.Changed"/> is
    /// raised on whichever thread committed the write, which is not necessarily this one; the
    /// composition root passes Avalonia's dispatcher, and a headless test runs it where it stands.
    /// </param>
    /// <param name="preview">
    /// The receipt template's "render to screen" capability (P1-T11). Null runs the screen
    /// without it - the template box still edits and saves, it simply has nothing to preview.
    /// </param>
    /// <param name="manualBackup">
    /// The owner's "Backup now" button (SRS FR-11.2, P1-T15). Null runs the Backup tab without it.
    /// </param>
    public SettingsViewModel(
        ISettings settings,
        IBackupPassphraseStore passphrases,
        Action<Action> onUiThread,
        IReceiptTemplatePreviewService? preview = null,
        IManualBackupTrigger? manualBackup = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(passphrases);
        ArgumentNullException.ThrowIfNull(onUiThread);

        _settings = settings;
        _passphrases = passphrases;
        _onUiThread = onUiThread;

        Receipt = preview is null ? new ReceiptSettingsViewModel() : new ReceiptSettingsViewModel(preview);
        Backup = new BackupSettingsViewModel(manualBackup);
        Backup.RestoreRequested += (_, e) => RestoreWizardRequested?.Invoke(this, e);

        Groups = [Shop, Financial, Tax, Numbering, Policy, Peripherals, Backup, Receipt];

        foreach (var group in Groups)
        {
            group.PropertyChanged += OnGroupEdited;

            foreach (var child in group.Children)
            {
                child.PropertyChanged += OnGroupEdited;
            }
        }

        _settings.Changed += OnSettingsChangedElsewhere;
    }

    /// <summary>Raised when the screen has nothing left to do and would like to be closed.</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Raised when the owner asks for the guided restore wizard (SRS FR-11.12).</summary>
    public event EventHandler? RestoreWizardRequested;

    /// <summary>FR-10.1.</summary>
    public ShopProfileSettingsViewModel Shop { get; } = new();

    /// <summary>FR-10.2.</summary>
    public FinancialSettingsViewModel Financial { get; } = new();

    /// <summary>FR-10.3.</summary>
    public TaxSettingsViewModel Tax { get; } = new();

    /// <summary>FR-10.4.</summary>
    public NumberingSettingsViewModel Numbering { get; } = new();

    /// <summary>FR-10.5.</summary>
    public PolicySettingsViewModel Policy { get; } = new();

    /// <summary>FR-10.6.</summary>
    public PeripheralSettingsViewModel Peripherals { get; } = new();

    /// <summary>FR-10.7.</summary>
    public BackupSettingsViewModel Backup { get; }

    /// <summary>FR-10.8.</summary>
    public ReceiptSettingsViewModel Receipt { get; }

    /// <summary>The eight groups, in the order FR-10 lists them.</summary>
    public IReadOnlyList<SettingsGroupViewModel> Groups { get; }

    /// <summary>
    /// Fills every group from the settings in force. Run when the window opens, when Revert is
    /// pressed, and after a save.
    /// </summary>
    [RelayCommand]
    public void Load()
    {
        _loaded = _settings.Current;

        foreach (var group in Groups)
        {
            group.Load(_loaded);
        }

        _discardConfirmed = false;
        HasUnsavedChanges = false;
        Status = "Showing the settings the shop is trading on.";
    }

    /// <summary>Writes every change in one go, and says what happened.</summary>
    [RelayCommand]
    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (Busy)
        {
            return;
        }

        foreach (var group in Groups)
        {
            if (group.Validate() is { } problem)
            {
                Status = problem;
                return;
            }
        }

        Busy = true;
        try
        {
            // The authorised call first, the passphrase only after it succeeds: a refused save
            // (a cashier reached this screen) must leave every existing backup restorable.
            // IBackupPassphraseStore.SetPassphrase is owner-only in its own right too - this
            // ordering is belt and braces, not the only guard.
            await _settings.SaveAsync(Build(), cancellationToken).ConfigureAwait(true);

            var passphraseStored = false;
            if (Backup.HasPassphraseToStore)
            {
                _passphrases.SetPassphrase(Backup.NewPassphrase);
                Backup.ClearPassphraseEntry();
                passphraseStored = true;

                // SaveAsync's own cache publish computed PassphraseIsSet before this write
                // happened. Re-read from disk so the screen shows the passphrase that is
                // actually there now, not the moment-earlier answer.
                await _settings.LoadAsync(cancellationToken).ConfigureAwait(true);
            }

            Load();
            Status = passphraseStored
                ? "Saved. The backup passphrase has been replaced."
                : "Saved.";
        }
        catch (NotAuthorisedException exception)
        {
            // The Application layer refused the caller, not the value. It is shown, not worked
            // around: the screen has no route to the settings that does not pass the check
            // (SRS §3.3 ROLE-2, NFR-S2, UI-06, AC-17). Caught deliberately and first, exactly as
            // UserAdminViewModel catches it - NotAuthorisedException is not an
            // InvalidOperationException, so a refusal cannot be swallowed by a handler written
            // for a business rule.
            Status = exception.Message;
        }
        catch (ArgumentException exception)
        {
            // The Application layer refused a value, in plain language. Show it; do not interpret
            // it, and do not save around it (SRS UI-06).
            Status = PlainLanguage(exception);
        }
        catch (InvalidOperationException exception)
        {
            Status = PlainLanguage(exception);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Cancelled. Nothing was changed.";
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Throws the edits away and shows what the shop is actually trading on.</summary>
    [RelayCommand]
    public void Revert()
    {
        Load();
        Status = "Your changes were thrown away. These are the settings in force.";
    }

    /// <summary>
    /// Escape. Closes the window, but not while there are unsaved edits and nobody has been told:
    /// the second press closes it (SRS UI-05 in miniature - say what will happen first).
    /// </summary>
    [RelayCommand]
    public void RequestClose()
    {
        if (HasUnsavedChanges && !_discardConfirmed)
        {
            _discardConfirmed = true;
            Status = "You have changes that are not saved. Press Ctrl+S to save them, or Escape "
                + "again to close without saving.";
            return;
        }

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The eight groups as one snapshot, ready for <see cref="ISettings.SaveAsync"/>.</summary>
    public SettingsSnapshot Build()
    {
        var snapshot = _loaded;

        foreach (var group in Groups)
        {
            snapshot = group.Apply(snapshot);
        }

        return snapshot;
    }

    public void Dispose()
    {
        _settings.Changed -= OnSettingsChangedElsewhere;

        foreach (var group in Groups)
        {
            group.PropertyChanged -= OnGroupEdited;

            foreach (var child in group.Children)
            {
                child.PropertyChanged -= OnGroupEdited;
            }
        }
    }

    /// <summary>
    /// The message without the argument name the framework appends to it. A shopkeeper reads
    /// "The paper width must be between 1 and 210"; "(Parameter 'value')" is for a stack trace.
    /// </summary>
    private static string PlainLanguage(Exception exception)
    {
        var message = exception.Message;
        var parameter = message.IndexOf(" (Parameter '", StringComparison.Ordinal);

        return parameter < 0 ? message : message[..parameter];
    }

    private void OnGroupEdited(object? sender, PropertyChangedEventArgs e)
    {
        _discardConfirmed = false;

        // Record equality, group by group. A passphrase waiting to be stored counts as an unsaved
        // change even though it is not part of the snapshot, because it is one.
        HasUnsavedChanges = Build() != _loaded || Backup.NewPassphrase.Length > 0;
    }

    /// <summary>
    /// Something else changed a setting. There is one till and one settings owner, so this is
    /// belt and braces - but a screen showing a value that is no longer in force is how a shop
    /// ends up arguing with its own receipt.
    /// </summary>
    private void OnSettingsChangedElsewhere(object? sender, EventArgs e) => _onUiThread(() =>
    {
        if (_settings.Current == _loaded)
        {
            // This screen's own save. Nothing to tell anybody about.
            return;
        }

        if (Busy || HasUnsavedChanges)
        {
            // Somebody is in the middle of typing. Do not pull the page out from under them.
            Status = "Another part of the till changed a setting. Press Escape and reopen this "
                + "screen to see it.";
            return;
        }

        Load();
    });
}
