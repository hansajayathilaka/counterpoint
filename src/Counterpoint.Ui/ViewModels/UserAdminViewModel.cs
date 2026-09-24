using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Ui.Services;

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
/// <para>
/// Task P3-T16 (SRS UI-15, AC-23): <see cref="NewUserDialogAsync"/> and
/// <see cref="ResetPasswordDialogAsync"/> open the retrofitted view's create/edit actions through
/// the shared <see cref="IDialogService"/> shell, each hosting its own small
/// <see cref="UserEditViewModel"/>. <b>The legacy <see cref="NewUsername"/>/<see cref="NewDisplayName"/>/
/// <see cref="NewPassword"/>/<see cref="NewUserIsOwner"/>/<see cref="CreateAsync"/>/
/// <see cref="ReplacementPassword"/>/<see cref="ResetPasswordAsync"/> family below is kept
/// byte-for-byte unchanged</b> - not because the retrofitted view still uses them (it does not),
/// but because the protected <c>LoginScreenTests</c> (SRS AC-17, FR-1.4) construct this type
/// directly with its original single-argument constructor and drive them without ever touching a
/// view, and this task's own done-when requires those tests to keep passing unmodified. They are
/// dead weight from the retrofitted view's point of view, left in place deliberately rather than
/// deleted, and documented as such rather than silently unused - the same choice task P3-T15 made
/// for <c>ProductTabViewModel</c>'s own legacy family.
/// </para>
/// </remarks>
public sealed partial class UserAdminViewModel : ViewModelBase
{
    private readonly IUserAdministration _users;
    private readonly IDialogService _dialogService;

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

    /// <param name="dialogService">
    /// Optional only so the protected <c>LoginScreenTests</c> - which predate task P3-T16 and
    /// construct this type directly with one positional argument - keep compiling and passing
    /// unmodified. Every real caller resolves this type through DI, which always supplies a real
    /// <see cref="IDialogService"/>; a screen that actually reaches one of the two dialog-opening
    /// commands below without one fails fast with a clear message rather than a null-reference
    /// exception (see <see cref="NoDialogService"/>).
    /// </param>
    public UserAdminViewModel(IUserAdministration users, IDialogService? dialogService = null)
    {
        ArgumentNullException.ThrowIfNull(users);
        _users = users;
        _dialogService = dialogService ?? NoDialogService.Instance;
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

    /// <summary>
    /// Opens the shared dialog to create a new account (SRS UI-15, AC-23) - the view's own New
    /// action, task P3-T16.
    /// </summary>
    [RelayCommand]
    public async Task NewUserDialogAsync(CancellationToken cancellationToken)
    {
        await RunAsync(
            async () =>
            {
                var content = new UserEditViewModel(
                    _users,
                    editingId: null,
                    initialUsername: string.Empty,
                    initialDisplayName: string.Empty,
                    initialIsOwner: false);

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Create,
                    "user",
                    subjectDescription: null,
                    content,
                    cancellationToken).ConfigureAwait(true);

                if (outcome == DialogOutcome.Confirmed)
                {
                    var created = content.Username.Trim();
                    await RefreshAsync(cancellationToken).ConfigureAwait(true);
                    Status = created + " can now sign in.";
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// Opens the shared dialog to set a new password on the selected account (SRS UI-15, AC-23) -
    /// the view's own Edit action, task P3-T16.
    /// </summary>
    [RelayCommand]
    public async Task ResetPasswordDialogAsync(CancellationToken cancellationToken)
    {
        if (SelectedUser is not { } selected)
        {
            Status = "Pick a user first.";
            return;
        }

        await RunAsync(
            async () =>
            {
                var content = new UserEditViewModel(
                    _users,
                    editingId: selected.Id,
                    initialUsername: selected.Username,
                    initialDisplayName: selected.DisplayName,
                    initialIsOwner: selected.RoleText == "Owner");

                var outcome = await _dialogService.ShowEditDialogAsync(
                    DialogMode.Edit,
                    "user",
                    subjectDescription: selected.Username,
                    content,
                    cancellationToken).ConfigureAwait(true);

                if (outcome == DialogOutcome.Confirmed)
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(true);
                    Status = selected.Username + " has a new password and is no longer locked.";
                }
            },
            cancellationToken).ConfigureAwait(true);
    }

    /// <summary>Creates an account. Legacy - see this type's remarks.</summary>
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

    /// <summary>
    /// Sets a new password on the selected account, which also clears any lockout. Legacy - see
    /// this type's remarks.
    /// </summary>
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

    /// <summary>
    /// Stands in for a real <see cref="IDialogService"/> only when this viewmodel is constructed
    /// without one - which happens only in the protected <c>LoginScreenTests</c> that predate task
    /// P3-T16 and never call a dialog-opening command. Every real, DI-resolved instance of this
    /// screen always receives a real <see cref="IDialogService"/> (registered in
    /// <c>Counterpoint.App</c>'s composition root); reaching this fallback in production would
    /// mean that wiring broke, so it fails fast with a clear message rather than a null-reference
    /// exception two frames further down.
    /// </summary>
    private sealed class NoDialogService : IDialogService
    {
        internal static readonly NoDialogService Instance = new();

        private NoDialogService()
        {
        }

        public Task<DialogOutcome> ShowEditDialogAsync<TViewModel>(
            DialogMode mode,
            string entityName,
            string? subjectDescription,
            TViewModel content,
            CancellationToken cancellationToken = default)
            where TViewModel : ViewModelBase, IEditDialogContent =>
            throw new InvalidOperationException(
                "UserAdminViewModel was constructed without an IDialogService and a dialog-opening "
                + "command was invoked. Resolve this screen through dependency injection.");

        public Task<DialogOutcome> ShowDeleteConfirmationAsync(
            string entityName,
            string subjectDescription,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "UserAdminViewModel was constructed without an IDialogService and a delete "
                + "confirmation was invoked. Resolve this screen through dependency injection.");

        public Task<DialogOutcome> ShowConfirmationAsync(
            string headerText,
            string message,
            string confirmButtonText,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "UserAdminViewModel was constructed without an IDialogService and a confirmation "
                + "was invoked. Resolve this screen through dependency injection.");
    }
}
