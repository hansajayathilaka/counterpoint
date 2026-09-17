using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Ui.Services;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// The content of task P3-T16's user create/reset-password dialog (SRS UI-15, AC-23, FR-1.4).
/// </summary>
/// <remarks>
/// <para>
/// One viewmodel serves both actions, distinguished by <see cref="IsCreate"/> (true exactly when
/// <c>editingId</c> is null): creating a new account collects a username, a display name, a
/// password and whether the account is an owner; resetting a password only ever needs the new
/// password, so <see cref="Views.UserEditView"/> hides the create-only fields when it is not.
/// Which header <c>EditDialogWindow</c> shows is <see cref="UserAdminViewModel"/>'s job, driven by
/// the explicit <see cref="DialogMode"/> it passes to <see cref="IDialogService"/> - never
/// inferred here.
/// </para>
/// <para>
/// There is no "edit a user's details" action on this till (SRS FR-1.4 only ever asks for create,
/// deactivate/reactivate and reset-password) - deactivate/reactivate stays the inline
/// <c>ToggleActiveCommand</c> button, the same choice task P3-T15 made for
/// <c>CategoryTabViewModel</c>'s own toggle-active action.
/// </para>
/// </remarks>
public sealed partial class UserEditViewModel : ViewModelBase, IEditDialogContent
{
    private readonly IUserAdministration _users;
    private readonly long? _editingId;

    [ObservableProperty]
    private string _username;

    [ObservableProperty]
    private string _displayName;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _isOwner;

    public UserEditViewModel(
        IUserAdministration users,
        long? editingId,
        string initialUsername,
        string initialDisplayName,
        bool initialIsOwner)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(initialUsername);
        ArgumentNullException.ThrowIfNull(initialDisplayName);

        _users = users;
        _editingId = editingId;
        _username = initialUsername;
        _displayName = initialDisplayName;
        _isOwner = initialIsOwner;
    }

    /// <summary>True when creating a brand new account; false when resetting an existing one's password.</summary>
    public bool IsCreate => _editingId is null;

    /// <summary>"Password" when creating, "New password" when resetting an existing account's.</summary>
    public string PasswordFieldLabel => IsCreate ? "Password" : "New password";

    public async Task<bool> SaveAsync(CancellationToken cancellationToken)
    {
        if (_editingId is { } id)
        {
            await _users.ResetPasswordAsync(id, Password, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _users.CreateAsync(
                new CreateUserCommand(Username, DisplayName, Password, IsOwner ? Role.Owner : Role.Cashier),
                cancellationToken).ConfigureAwait(true);
        }

        return true;
    }
}
