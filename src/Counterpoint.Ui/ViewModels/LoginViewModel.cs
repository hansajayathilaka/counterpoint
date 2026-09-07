using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Security;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// The sign-in screen (SRS FR-1.1). Nothing runs until somebody gets past it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It decides nothing.</b> It does not compare a password, count a failure, know what a
/// lockout is or how long one lasts. It hands two strings to
/// <see cref="IAuthenticationService"/> and shows the sentence that comes back
/// (SRS UI-06, NFR-S2). That is why the message is composed in the Application layer: the layer
/// that knows how long the lock has left is the layer that can say so.
/// </para>
/// <para>
/// <b>The first-run branch.</b> A brand new database has an owner account with no usable
/// password, because there is no default password anywhere in this system. The screen offers to
/// set one, exactly once, through <see cref="IInitialOwnerSetup"/> - which refuses the moment a
/// usable credential exists. The rest of the first-run wizard is P1-T03.
/// </para>
/// </remarks>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly IAuthenticationService _authentication;
    private readonly IInitialOwnerSetup _initialSetup;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string _status = "Sign in to start.";

    [ObservableProperty]
    private bool _busy;

    /// <summary>True when this database has no usable credential yet and one must be set.</summary>
    [ObservableProperty]
    private bool _setupRequired;

    public LoginViewModel(IAuthenticationService authentication, IInitialOwnerSetup initialSetup)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        ArgumentNullException.ThrowIfNull(initialSetup);

        _authentication = authentication;
        _initialSetup = initialSetup;
    }

    /// <summary>
    /// Raised once a password has verified. The composition root listens for it and opens the
    /// sales screen; the viewmodel does not know what a window is.
    /// </summary>
    public event EventHandler? SignedIn;

    /// <summary>
    /// Asks the Application layer whether the shop has a usable credential yet, and puts the
    /// screen into the first-run branch if not.
    /// </summary>
    [RelayCommand]
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                SetupRequired = await _initialSetup.IsRequiredAsync(cancellationToken).ConfigureAwait(true);

                if (SetupRequired)
                {
                    Username = "owner";
                    Status = "This till has no owner password yet. Set one to continue.";
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Verifies the password and, on success, hands over to the sales screen.</summary>
    [RelayCommand]
    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        if (Busy || SetupRequired)
        {
            return;
        }

        await RunAsync(
            async () =>
            {
                var result = await _authentication
                    .LogInAsync(Username.Trim(), Password, cancellationToken)
                    .ConfigureAwait(true);

                // Cleared whatever happened. A password left in a bound property is a password
                // sitting in memory behind an unattended screen.
                Password = string.Empty;
                Status = result.Message;

                if (result.Succeeded)
                {
                    SignedIn?.Invoke(this, EventArgs.Empty);
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Sets the shop's first owner password, then signs in with it.</summary>
    [RelayCommand]
    public async Task SetOwnerPasswordAsync(CancellationToken cancellationToken)
    {
        if (Busy || !SetupRequired)
        {
            return;
        }

        if (!string.Equals(Password, ConfirmPassword, StringComparison.Ordinal))
        {
            Status = "The two passwords are not the same. Type them again.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var password = Password;

                await _initialSetup.CompleteAsync(Username.Trim(), password, cancellationToken)
                    .ConfigureAwait(true);

                ConfirmPassword = string.Empty;
                SetupRequired = false;

                var result = await _authentication
                    .LogInAsync(Username.Trim(), password, cancellationToken)
                    .ConfigureAwait(true);

                Password = string.Empty;
                Status = result.Message;

                if (result.Succeeded)
                {
                    SignedIn?.Invoke(this, EventArgs.Empty);
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs an Application call with the screen locked, and turns anything that comes back into a
    /// sentence the person at the counter can act on (SRS UI-06).
    /// </summary>
    private async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        Busy = true;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (NotAuthorisedException exception)
        {
            Status = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            // The Application layer said no, in plain language. Show it; do not interpret it.
            Status = exception.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Cancelled.";
        }
        finally
        {
            Busy = false;
        }
    }
}
