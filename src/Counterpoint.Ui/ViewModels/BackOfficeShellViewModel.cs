using System;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// The back office's own navigation: Catalogue, Settings, Users, Purchasing and Labels
/// (SRS UI-11, NFR-S2, AC-17, AC-24, task P3-T13).
/// </summary>
/// <remarks>
/// <para>
/// <b>One process, one session, no second door.</b> This viewmodel reads the exact same
/// <see cref="ISession"/> singleton <see cref="SalesViewModel"/> reads - the one P1-T02
/// established - so a role change (signing out and back in as someone else) is visible here
/// immediately, with nothing of its own to keep in sync. There is no second login, no second
/// connection, no second anything: the composition root hands this viewmodel the same
/// <see cref="ISession"/> instance it hands the sales screen.
/// </para>
/// <para>
/// <b>Hiding a tile is a courtesy, not the control.</b> Every command below fires an event a view
/// reacts to by opening a screen; every one of those screens is bound to an Application-layer
/// interface carrying <see cref="RequiresRoleAttribute"/>, checked again there regardless of
/// whether this viewmodel ever let the tile render (SRS NFR-S2, AC-17, AC-24) - see
/// <c>RoleAuthorisation</c> and the per-screen AC-17 tests already covering each of the five
/// destinations (<c>CatalogueAuthorisationTests</c>, <c>SettingsScreenTests</c>,
/// <c>UserAdministrationTests</c>, <c>PurchaseOrderServiceTests</c>,
/// <c>LabelPrintServiceTests</c>).
/// </para>
/// </remarks>
public sealed partial class BackOfficeShellViewModel : ViewModelBase
{
    private readonly ISession _session;

    public BackOfficeShellViewModel(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    // ---- Status bar (SRS UI-09, UI-11: its own, distinct from the sales screen's) --------------

    public string StatusUserText => _session.CurrentUser is { } user
        ? user.DisplayName + " (" + (user.Role == Role.Owner ? "owner" : "cashier") + ")"
        : "Not signed in";

    public string StatusShiftText => _session.ShiftId is { } shiftId
        ? "Shift #" + shiftId.ToString(CultureInfo.InvariantCulture)
        : "No shift open";

    // ---- Tile visibility (a courtesy - see remarks above) ----------------------------------------

    public bool CanManageUsers => _session.CurrentUser?.Role == Role.Owner;

    public bool CanChangeSettings => _session.CurrentUser?.Role == Role.Owner;

    public bool CanManageCatalogue => _session.CurrentUser?.Role == Role.Owner;

    public bool CanPrintLabels => _session.CurrentUser?.Role == Role.Owner;

    public bool CanManagePurchasing => _session.CurrentUser?.Role == Role.Owner;

    // ---- Navigation - a view concern; opening a window is the composition root's job -----------

    /// <summary>Raised when the owner asks for the user-management screen.</summary>
    public event EventHandler? ManageUsersRequested;

    /// <summary>Raised when the owner asks for the settings screen (SRS FR-10).</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the owner asks for the catalogue reference-data screen.</summary>
    public event EventHandler? CatalogueRequested;

    /// <summary>Raised when the owner asks for the label-printing screen.</summary>
    public event EventHandler? LabelPrintRequested;

    /// <summary>Raised when the owner asks for the purchase-order screen (SRS FR-4.5, FR-4.6, FR-4.10).</summary>
    public event EventHandler? PurchaseOrdersRequested;

    [RelayCommand]
    public void ManageUsers() => ManageUsersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ManageCatalogue() => CatalogueRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void PrintLabels() => LabelPrintRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ManagePurchaseOrders() => PurchaseOrdersRequested?.Invoke(this, EventArgs.Empty);
}
