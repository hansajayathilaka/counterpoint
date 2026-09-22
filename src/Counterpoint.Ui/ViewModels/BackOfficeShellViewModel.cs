using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Ui.ViewModels.Catalogue;

namespace Counterpoint.Ui.ViewModels;

/// <summary>
/// The back office's own navigation: a persistent nav rail grouped into Overview, Catalogue,
/// Trading, People and System (SRS UI-11, UI-16, NFR-S2, AC-17, AC-24, tasks P3-T13/P3-T18).
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
/// <b>Hiding a nav item is a courtesy, not the control.</b> Every command below either fires an
/// event a view reacts to by opening a screen (Trading/People/System - unchanged from task
/// P3-T13), or swaps <see cref="SelectedCatalogueSection"/> to change what the shell's own
/// content pane shows (Catalogue - new in task P3-T18). Either way, every destination is bound to
/// an Application-layer interface carrying <see cref="RequiresRoleAttribute"/>, checked again
/// there regardless of whether this viewmodel ever let the nav item render (SRS NFR-S2, AC-17,
/// AC-24) - see <c>RoleAuthorisation</c> and the per-screen AC-17 tests already covering each
/// destination (<c>CatalogueAuthorisationTests</c>, <c>SettingsScreenTests</c>,
/// <c>UserAdministrationTests</c>, <c>PurchaseOrderServiceTests</c>,
/// <c>LabelPrintServiceTests</c>).
/// </para>
/// <para>
/// <b>Task P3-T18 risk mitigation.</b> The mapping from yesterday's five flat tile buttons to
/// today's nav-rail items is unchanged, item for item: Catalogue -&gt; <see cref="CanManageCatalogue"/>,
/// Settings -&gt; <see cref="CanChangeSettings"/>, Users -&gt; <see cref="CanManageUsers"/>, Purchase
/// orders -&gt; <see cref="CanManagePurchasing"/>, Labels -&gt; <see cref="CanPrintLabels"/>. Nothing in
/// this task introduces a new authorisation flag or repurposes an existing one.
/// </para>
/// </remarks>
public sealed partial class BackOfficeShellViewModel : ViewModelBase
{
    /// <summary>
    /// The eight Catalogue destinations, in the exact order <c>CatalogueWindow</c>'s old
    /// <c>TabControl</c> presented them (SRS FR-2.20, FR-2.21, FR-6). Each name doubles as the
    /// value <see cref="SelectedCatalogueSection"/> carries and as the section key
    /// <c>CatalogueSectionContent</c> compares against to decide which tab to show.
    /// </summary>
    public static readonly IReadOnlyList<string> CatalogueSectionNames =
    [
        "Categories",
        "Brands",
        "Units",
        "Tax classes",
        "Suppliers",
        "Customers",
        "Products",
        "Import / Export",
    ];

    private readonly ISession _session;
    private bool _catalogueLoaded;

    public BackOfficeShellViewModel(ISession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    // ---- Catalogue folded into the content pane (task P3-T18) ------------------------------------

    /// <summary>The eight Catalogue nav-rail items, for the rail's <c>ItemsSource</c>.</summary>
    public IReadOnlyList<string> CatalogueSections { get; } = CatalogueSectionNames;

    /// <summary>
    /// Which Catalogue tab the content pane shows, or null when the pane shows Overview instead.
    /// Replaces <c>CatalogueWindow.axaml</c>'s old <c>TabControl.SelectedIndex</c> - the nav
    /// rail's Catalogue <c>ListBox</c> binds its <c>SelectedItem</c> straight to this property,
    /// two-way, the same way a <c>TabControl</c>'s own tab strip drove its content before.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCatalogueSectionActive))]
    private string? _selectedCatalogueSection;

    /// <summary>True once a Catalogue tab is showing in the content pane, false for Overview.</summary>
    public bool IsCatalogueSectionActive => SelectedCatalogueSection is not null;

    /// <summary>
    /// The catalogue reference-data screen's own viewmodel, attached once by the composition root
    /// after construction (see <see cref="AttachCatalogue"/>) rather than taken as a constructor
    /// parameter - so every test that builds this viewmodel directly from just an
    /// <see cref="ISession"/>, to prove nav-item gating or drive the Application layer past this
    /// viewmodel entirely (SRS AC-17, AC-24), keeps working unmodified. Which viewmodel backs the
    /// Catalogue section is a wiring detail, not part of what those tests prove.
    /// </summary>
    public CatalogueViewModel? Catalogue { get; private set; }

    /// <summary>Wired once by the composition root, immediately after both singletons resolve.</summary>
    public void AttachCatalogue(CatalogueViewModel catalogue)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        Catalogue = catalogue;
    }

    /// <summary>
    /// Loads the catalogue's reference data the first time a Catalogue section is actually
    /// selected, not when the shell itself opens - the same "defer loading until navigated to"
    /// rule <c>ShowCatalogue</c> used to satisfy simply by not existing until the Catalogue tile
    /// was clicked (SRS NFR-P6, the avalonia-pos-screens skill's cold-start guidance).
    /// </summary>
    partial void OnSelectedCatalogueSectionChanged(string? value)
    {
        if (value is not null && !_catalogueLoaded && Catalogue is not null)
        {
            _catalogueLoaded = true;
            Catalogue.LoadCommand.Execute(null);
        }
    }

    /// <summary>Returns the content pane to Overview (SRS UI-16 - pending real content, P3-T20).</summary>
    [RelayCommand]
    public void SelectOverview() => SelectedCatalogueSection = null;

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

    /// <summary>
    /// Whether the rail's Trading group header has anything under it to show. Not a new
    /// authorisation flag of its own - purely an OR of the two existing flags below, so an empty
    /// group heading never renders; it changes no per-item gating decision (see the class remarks'
    /// "risk mitigation" note).
    /// </summary>
    public bool CanSeeTradingGroup => CanManagePurchasing || CanPrintLabels;

    // ---- Navigation - a view concern; opening a window is the composition root's job -----------

    /// <summary>Raised when the owner asks for the user-management screen.</summary>
    public event EventHandler? ManageUsersRequested;

    /// <summary>Raised when the owner asks for the settings screen (SRS FR-10).</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>Raised when the owner asks for the label-printing screen.</summary>
    public event EventHandler? LabelPrintRequested;

    /// <summary>Raised when the owner asks for the purchase-order screen (SRS FR-4.5, FR-4.6, FR-4.10).</summary>
    public event EventHandler? PurchaseOrdersRequested;

    [RelayCommand]
    public void ManageUsers() => ManageUsersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void OpenSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void PrintLabels() => LabelPrintRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ManagePurchaseOrders() => PurchaseOrdersRequested?.Invoke(this, EventArgs.Empty);
}
