using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Security;

namespace Counterpoint.Ui.ViewModels.Settings;

/// <summary>
/// The guided restore wizard: pick a backup, verify it, type the passphrase, see the data date,
/// type an explicit confirmation, and restore (SRS FR-11.12, FR-11.13, P1-T15).
/// </summary>
/// <remarks>
/// <para>
/// One screen, four steps in order, each gating the next: browse to a file, check it (reads the
/// header and verifies the checksum, without a passphrase), type the passphrase and the
/// confirmation phrase, then restore. The order matches FR-11.12's own list.
/// </para>
/// <para>
/// A successful restore does not touch the live database - it stages the result and asks
/// Counterpoint to be restarted (see <c>Counterpoint.Backup.Restore.PendingRestoreLocation</c>).
/// <see cref="RestartRequested"/> is how this screen tells its window that the till should close;
/// closing it is a view concern, the same reasoning <c>Ui.App</c> already applies to every other
/// window transition.
/// </para>
/// </remarks>
public sealed partial class RestoreWizardViewModel : ViewModelBase
{
    private readonly IGuidedRestoreService _restore;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private string _passphrase = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private string _typedConfirmation = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private bool _busy;

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreCommand))]
    private bool _hasVerifiedPreview;

    [ObservableProperty]
    private bool _restoreCompleted;

    public RestoreWizardViewModel(IGuidedRestoreService restore)
    {
        ArgumentNullException.ThrowIfNull(restore);
        _restore = restore;
    }

    /// <summary>What the owner must type to confirm - shown on screen next to the box.</summary>
    public string RequiredConfirmationPhrase { get; } = GuidedRestoreConfirmation.RequiredPhrase;

    /// <summary>Raised once the restore is staged, asking the till to close and be started again.</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Forgets everything typed and previewed, for a fresh attempt or a fresh file.</summary>
    public void Reset()
    {
        Passphrase = string.Empty;
        TypedConfirmation = string.Empty;
        PreviewText = string.Empty;
        HasVerifiedPreview = false;
        RestoreCompleted = false;
        Status = string.Empty;
    }

    /// <summary>
    /// Step 1: reads the header and verifies the checksum of <see cref="FilePath"/>, without a
    /// passphrase (SRS FR-11.12's "verify checksum" and "show what date the data will be restored
    /// to", both before the passphrase is asked for).
    /// </summary>
    [RelayCommand]
    private async Task CheckBackupAsync(CancellationToken cancellationToken)
    {
        if (Busy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(FilePath))
        {
            Status = "Choose a backup file first.";
            return;
        }

        Busy = true;
        HasVerifiedPreview = false;
        PreviewText = string.Empty;

        try
        {
            var preview = await _restore.PreviewAsync(FilePath, cancellationToken).ConfigureAwait(true);

            PreviewText = "This backup was taken on "
                + preview.TakenAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + " (schema " + preview.SchemaVersion + "). Its checksum is intact.";
            HasVerifiedPreview = true;
            Status = "Checked. Type the passphrase and " + RequiredConfirmationPhrase + " below to restore.";
        }
        catch (ArgumentException exception)
        {
            Status = PlainLanguage(exception);
        }
        catch (InvalidOperationException exception)
        {
            Status = PlainLanguage(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>
    /// Step 2: backs up the current database, decrypts the chosen backup, and stages it (SRS
    /// FR-11.12). Only reachable once <see cref="HasVerifiedPreview"/> is true and the typed
    /// confirmation matches exactly - CanExecute, not merely a validation message, so there is no
    /// path from a button click alone to a restore (FR-11.12's "explicit typed confirmation").
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRestore))]
    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (Busy)
        {
            return;
        }

        Busy = true;
        try
        {
            var outcome = await _restore.RestoreAsync(
                new GuidedRestoreRequest(FilePath, Passphrase, TypedConfirmation),
                cancellationToken).ConfigureAwait(true);

            Status = "Restored. The current database was backed up first, as " + outcome.SafetyBackupFilename
                + ". Counterpoint must be closed and started again to trade on the restored data ("
                + outcome.RestoredDataDate.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                + ").";
            RestoreCompleted = true;
        }
        catch (ArgumentException exception)
        {
            Status = PlainLanguage(exception);
        }
        catch (NotAuthorisedException exception)
        {
            Status = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            Status = PlainLanguage(exception);
        }
        finally
        {
            Busy = false;
        }
    }

    private bool CanRestore() =>
        HasVerifiedPreview
        && Passphrase.Length > 0
        && string.Equals(TypedConfirmation, RequiredConfirmationPhrase, StringComparison.Ordinal);

    /// <summary>The owner has read the warning and wants Counterpoint to close now.</summary>
    [RelayCommand]
    private void CloseNow() => RestartRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The message without the argument name the framework appends to it (SRS UI-06).</summary>
    private static string PlainLanguage(Exception exception)
    {
        var message = exception.Message;
        var parameter = message.IndexOf(" (Parameter '", StringComparison.Ordinal);

        return parameter < 0 ? message : message[..parameter];
    }
}
