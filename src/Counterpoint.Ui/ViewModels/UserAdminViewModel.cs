using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// The owner's user-management screen: create, deactivate, reset a password (SRS FR-1.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The screen does not check anything.</b> It calls <see cref="IUserAdministration"/>, whose
/// every method is owner-only, and shows what comes back - including the refusal. The check runs
/// in front of that service in the Application layer, so this screen being reachable would not
/// grant a cashier a single thing (SRS NFR-S2, AC-17). Hiding the button that opens it is a
/// courtesy, not the control.
/// </para>
/// <para>
/// Deliberately plain, as the sales screen is: this is the shape of the operations, not the shape
/// of the finished till.
/// </para>
/// </remarks>
public sealed partial class UserAdminViewModel : ViewModelBase
{
    private readonly IUserAdministration _users;

    [ObservableProperty]
    private string _newUsername = string.Empty;

    [ObservableProperty]
    private string _newDisplayName = string.Empty;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    private bool _newUserIsOwner;

    [ObservableProperty]
    private UserRowViewModel? _selectedUser;

    [ObservableProperty]
    private string _replacementPassword = string.Empty;

    [ObservableProperty]
    private string _status = "Loading users...";

    [ObservableProperty]
    private bool _busy;

    public UserAdminViewModel(IUserAdministration users)
    {
        ArgumentNullException.ThrowIfNull(users);
        _users = users;
    }

    /// <summary>Every account on the till, in username order.</summary>
    public ObservableCollection<UserRowViewModel> Users { get; } = [];

    /// <summary>Reloads the list.</summary>
    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var users = await _users.ListAsync(cancellationToken).ConfigureAwait(true);

                var selectedId = SelectedUser?.Id;

                Users.Clear();
                foreach (var user in users)
                {
                    Users.Add(new UserRowViewModel(user));
                }

                SelectedUser = null;
                foreach (var row in Users)
                {
                    if (row.Id == selectedId)
                    {
                        SelectedUser = row;
                        break;
                    }
                }

                Status = Users.Count == 1 ? "1 user." : Users.Count + " users.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Creates an account.</summary>
    [RelayCommand]
    public async Task CreateAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                await _users.CreateAsync(
                    new CreateUserCommand(
                        NewUsername,
                        NewDisplayName,
                        NewPassword,
                        NewUserIsOwner ? Role.Owner : Role.Cashier),
                    cancellationToken).ConfigureAwait(true);

                var created = NewUsername.Trim();

                NewUsername = string.Empty;
                NewDisplayName = string.Empty;
                NewPassword = string.Empty;
                NewUserIsOwner = false;

                await RefreshAsync(cancellationToken).ConfigureAwait(true);

                Status = created + " can now sign in.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Turns the selected account off, or back on if it is already off.</summary>
    [RelayCommand]
    public async Task ToggleActiveAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is not { } selected)
        {
            Status = "Pick a user first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                if (selected.Active)
                {
                    await _users.DeactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await _users.ReactivateAsync(selected.Id, cancellationToken).ConfigureAwait(true);
                }

                var wasActive = selected.Active;

                await RefreshAsync(cancellationToken).ConfigureAwait(true);

                Status = selected.Username + (wasActive ? " is turned off." : " is turned back on.");
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Sets a new password on the selected account, which also clears any lockout.</summary>
    [RelayCommand]
    public async Task ResetPasswordAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is not { } selected)
        {
            Status = "Pick a user first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                await _users.ResetPasswordAsync(selected.Id, ReplacementPassword, cancellationToken)
                    .ConfigureAwait(true);

                ReplacementPassword = string.Empty;

                await RefreshAsync(cancellationToken).ConfigureAwait(true);

                Status = selected.Username + " has a new password and is no longer locked.";
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Runs an Application call with the screen locked, and turns anything that comes back into a
    /// sentence the owner can act on (SRS UI-06).
    /// </summary>
    private async Task RunAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        var alreadyBusy = Busy;
        Busy = true;
        try
        {
            await operation().ConfigureAwait(true);
        }
        catch (NotAuthorisedException exception)
        {
            // The Application layer refused. It is shown, not worked around: the screen has no
            // route to the service that does not pass the check.
            Status = exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            Status = exception.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Status = "Cancelled.";
        }
        finally
        {
            Busy = alreadyBusy;
        }
    }
}
