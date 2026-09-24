using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Ui.Services;
using Counterpoint.Ui.ViewModels.Catalogue;
using Counterpoint.Ui.ViewModels.Dashboard;
using Counterpoint.Ui.ViewModels.Settings;

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
/// event a view reacts to by opening a screen (Trading/People - unchanged from task P3-T13), or
/// swaps <see cref="SelectedCatalogueSection"/>/<see cref="SelectedSettingsSection"/> to change
/// what the shell's own content pane shows (Catalogue - task P3-T18; System/Settings - task
/// P3-T19). Either way, every destination is bound to an Application-layer interface carrying
/// <see cref="RequiresRoleAttribute"/>, checked again there regardless of whether this viewmodel
/// ever let the nav item render (SRS NFR-S2, AC-17, AC-24) - see <c>RoleAuthorisation</c> and the
/// per-screen AC-17 tests already covering each destination (<c>CatalogueAuthorisationTests</c>,
/// <c>SettingsScreenTests</c>, <c>UserAdministrationTests</c>, <c>PurchaseOrderServiceTests</c>,
/// <c>LabelPrintServiceTests</c>).
/// </para>
/// <para>
/// <b>Task P3-T18/P3-T19 risk mitigation.</b> The mapping from yesterday's five flat tile buttons
/// to today's nav-rail items is unchanged, item for item: Catalogue -&gt; <see cref="CanManageCatalogue"/>,
/// Settings -&gt; <see cref="CanChangeSettings"/>, Users -&gt; <see cref="CanManageUsers"/>, Purchase
/// orders -&gt; <see cref="CanManagePurchasing"/>, Labels -&gt; <see cref="CanPrintLabels"/>. Nothing in
/// either task introduces a new authorisation flag or repurposes an existing one.
/// </para>
/// <para>
/// <b>Task P3-T19's own named risk, and how this class closes it.</b> Leaving the System group
/// (Overview or Catalogue selected) while <see cref="SettingsViewModel.HasUnsavedChanges"/> is
/// true must not silently lose an edit. <see cref="SelectedCatalogueSection"/>'s setter and
/// <see cref="SelectOverview"/> both check this before applying a navigation away from System; if
/// there is something to lose, the change is reverted immediately (before anything renders) and
/// <see cref="IDialogService.ShowConfirmationAsync"/> - the exact same P3-T11 dialog shell every
/// other confirmation in the back office already uses - is asked first. Confirming discards the
/// edit (<see cref="SettingsViewModel.RevertCommand"/>) and completes the navigation; cancelling
/// leaves both properties exactly as they were, with the edit intact. Switching between System's
/// own nine sub-items never asks - it is one screen, one save, the same as
/// <c>SettingsWindow.axaml</c>'s old <c>TabControl</c> never asked when the tab strip moved.
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

    /// <summary>
    /// The nine settings groups, in the exact order <c>SettingsWindow</c>'s old <c>TabControl</c>
    /// presented them (SRS FR-10.1-10.9, UI-13) - <see cref="SettingsViewModel.Groups"/>'s own
    /// order. Each name doubles as the value <see cref="SelectedSettingsSection"/> carries and as
    /// the section key <c>SettingsSectionContent</c> compares against to decide which group to
    /// show (task P3-T19).
    /// </summary>
    public static readonly IReadOnlyList<string> SettingsSectionNames =
    [
        "Shop profile",
        "Financial",
        "Tax",
        "Numbering",
        "Policy",
        "Peripherals",
        "Backup",
        "Receipt",
        "Display",
    ];

    private readonly ISession _session;

    /// <summary>
    /// The P3-T11 confirmation shell, used only by the navigate-away-from-System guard (task
    /// P3-T19). Attached after construction, not taken as a constructor parameter, for exactly
    /// the reason <see cref="AttachCatalogue"/> gives: every test that builds this viewmodel
    /// directly from just an <see cref="ISession"/> keeps working unmodified. Null in those tests
    /// means the guard's dialog branch is simply never reached, because it is reached only when
    /// <see cref="Settings"/> is also attached and holds unsaved changes.
    /// </summary>
    private IDialogService? _dialogService;

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
    [NotifyPropertyChangedFor(nameof(IsOverviewActive))]
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

    // ---- Overview: the real dashboard content (task P3-T20) -------------------------------------

    /// <summary>
    /// The Overview nav item's own viewmodel (task P3-T20, replacing task P3-T18's placeholder) -
    /// attached the same way <see cref="Catalogue"/> is (see <see cref="AttachDashboard"/>'s own
    /// remarks), for the same reason: every test that builds this viewmodel directly from just an
    /// <see cref="ISession"/> keeps working unmodified.
    /// </summary>
    public DashboardViewModel? Dashboard { get; private set; }

    /// <summary>Wired once by the composition root, immediately after both singletons resolve.</summary>
    public void AttachDashboard(DashboardViewModel dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        Dashboard = dashboard;
    }

    /// <summary>
    /// Loads the catalogue's reference data every time a Catalogue section is selected coming
    /// from Overview - i.e. every genuine re-entry into Catalogue - but not when the selection
    /// merely moves between Catalogue sections while already there.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the "defer loading until navigated to, then re-read on every real visit" rule
    /// <c>ShowCatalogue</c> used to satisfy simply by not existing between visits: its window (and
    /// the <c>CatalogueViewModel</c> singleton it drove) was gone whenever Catalogue wasn't open,
    /// so every click on the Catalogue tile was, by construction, a transition out of "not open" -
    /// the same transition <paramref name="oldValue"/> being null captures here now that
    /// <see cref="Catalogue"/> is a singleton that outlives any one visit (SRS NFR-P6, the
    /// avalonia-pos-screens skill's cold-start guidance).
    /// </para>
    /// <para>
    /// Closing and reopening the back-office window is a genuine re-entry too, even when the same
    /// nav-rail item ends up selected again on the way back in - see <c>App.axaml.cs</c>'s
    /// <c>ShowBackOffice</c>, which calls <see cref="ResetNavigation"/> on window close for exactly
    /// this reason, so the next section picked after reopening always arrives with
    /// <paramref name="oldValue"/> null.
    /// </para>
    /// <para>
    /// Task P3-T20 extends this with the reverse transition: leaving Catalogue for Overview
    /// (<paramref name="newValue"/> null, <paramref name="oldValue"/> not) re-reads
    /// <see cref="Dashboard"/>'s three queries, the same "reload on every real visit" rule the
    /// Catalogue half already follows - a stock edit made while in Catalogue should not leave a
    /// stale low-stock count showing on the way back out.
    /// </para>
    /// <para>
    /// <b>Task P3-T19 addition.</b> Selecting a Catalogue section while System is active is also a
    /// "navigate away from System" attempt - see <see cref="TryLeaveSystem"/>, called first, below.
    /// When it intercepts, this setter's own new value is reverted synchronously (see
    /// <see cref="_suppressCatalogueGuard"/>) before anything below runs, so neither the mutual-
    /// exclusion clear nor the reload fires until (and unless) the guard's own confirmation
    /// resolves in favour of leaving.
    /// </para>
    /// </remarks>
    partial void OnSelectedCatalogueSectionChanged(string? oldValue, string? newValue)
    {
        if (_suppressCatalogueGuard)
        {
            return;
        }

        if (newValue is not null
            && TryLeaveSystem(
                revertNow: () =>
                {
                    // Reverting synchronously back to null (there is no other value
                    // SelectedCatalogueSection can hold while System is active) re-enters this
                    // very method with newValue null, which satisfies none of the conditions
                    // below - nothing else to suppress.
                    _suppressCatalogueGuard = true;
                    try
                    {
                        SelectedCatalogueSection = oldValue;
                    }
                    finally
                    {
                        _suppressCatalogueGuard = false;
                    }
                },
                reapply: () => SelectedCatalogueSection = newValue))
        {
            return;
        }

        if (newValue is not null && SelectedSettingsSection is not null)
        {
            SelectedSettingsSection = null;
        }

        if (newValue is not null && oldValue is null && Catalogue is not null)
        {
            Catalogue.LoadCommand.Execute(null);
        }
        else if (newValue is null && oldValue is not null && Dashboard is not null)
        {
            Dashboard.LoadCommand.Execute(null);
        }
    }

    /// <summary>
    /// Returns the content pane to Overview (SRS UI-16, task P3-T20's real dashboard content),
    /// asking first (task P3-T19) if that means leaving System with something unsaved. Either way
    /// back to Overview - the guard's own <c>reapply</c> or the direct fall-through below - also
    /// reloads <see cref="Dashboard"/>, the same "reload on every real visit" rule
    /// <see cref="OnSelectedCatalogueSectionChanged"/> already applies when leaving Catalogue for
    /// Overview: a settings change (e.g. reorder point) made while in System should not leave a
    /// stale dashboard showing on the way back out either.
    /// </summary>
    [RelayCommand]
    public void SelectOverview()
    {
        if (TryLeaveSystem(
            revertNow: () =>
            {
                // Nothing has changed yet - unlike the Catalogue ListBox's two-way binding, this
                // command runs before either property is touched, so there is nothing to revert.
            },
            reapply: () =>
            {
                SelectedCatalogueSection = null;
                SelectedSettingsSection = null;
                Dashboard?.LoadCommand.Execute(null);
            }))
        {
            return;
        }

        SelectedCatalogueSection = null;
        SelectedSettingsSection = null;
        Dashboard?.LoadCommand.Execute(null);
    }

    /// <summary>
    /// Unconditionally returns the content pane to Overview, bypassing the task P3-T19
    /// navigate-away guard entirely. Used only when the whole back-office window itself is
    /// closing (see <c>App.axaml.cs</c>'s <c>ShowBackOffice</c>) - there is no window left to show
    /// a confirmation dialog against at that point, and losing an in-progress, never-saved
    /// settings edit when the owner closes the entire back office (as opposed to clicking to
    /// another nav group while it stays open) is the same risk <c>SettingsWindow</c>'s own
    /// Escape-with-confirmation guard carried before this task: it only ever protected against
    /// closing System's own window, never against a parent window closing over the top of it.
    /// </summary>
    [RelayCommand]
    public void ResetNavigation()
    {
        SelectedCatalogueSection = null;
        SelectedSettingsSection = null;
    }

    /// <summary>
    /// True while <see cref="SelectedCatalogueSection"/>'s own setter is reverting a value it just
    /// applied, because leaving System turned out to need confirmation first (see
    /// <see cref="OnSelectedCatalogueSectionChanged"/>). Prevents that revert - and the later
    /// reapply once the operator confirms - from re-running the guard against itself.
    /// </summary>
    private bool _suppressCatalogueGuard;

    /// <summary>
    /// The one place task P3-T19's navigate-away guard is decided. Returns <see langword="false"/>
    /// immediately - nothing to intercept - when System is not the active group, or when it is but
    /// <see cref="SettingsViewModel.HasUnsavedChanges"/> is false. Otherwise runs
    /// <paramref name="revertNow"/> synchronously, before anything else - restoring a two-way-bound
    /// property that has already applied the new value, or doing nothing for a command that has
    /// not touched anything yet - and only then starts the async confirm-then-<paramref name="reapply"/>
    /// flow (fire-and-forget: this method itself must stay synchronous, because the property
    /// setter calling it cannot await), before returning <see langword="true"/> so the caller
    /// knows to stop.
    /// </summary>
    /// <remarks>
    /// <paramref name="revertNow"/> must run <b>before</b> <see cref="ConfirmDiscardAndNavigateAsync"/>
    /// is even started, not after - <see cref="IDialogService.ShowConfirmationAsync"/> carries no
    /// guarantee that it always yields (a real modal dialog does, but nothing stops a future
    /// implementation, or a test double such as the one <c>FakeDialogService</c> hands every
    /// non-UI test in this solution, from resolving its <see cref="Task"/> already completed). If
    /// the confirmation resolves synchronously, its own continuation - <see cref="Settings"/>'
    /// <see cref="SettingsViewModel.RevertCommand"/> and then <paramref name="reapply"/> - runs to
    /// completion inside this very call, before this method returns. A caller-side revert written
    /// to run <i>after</i> calling this method, as this used to be structured, would then stomp
    /// back over that already-correct, already-confirmed result the instant it got control back -
    /// discarding a confirmed navigation back to whatever it was before, with no error and no
    /// visible failure. Running <paramref name="revertNow"/> first, inside this method, before the
    /// confirmation is ever started, is correct regardless of whether the confirmation resolves
    /// synchronously or genuinely asynchronously: the real (asynchronous) case is unaffected, since
    /// nothing observable happens between "revert" and "start the confirmation" either way.
    /// </remarks>
    private bool TryLeaveSystem(Action revertNow, Action reapply)
    {
        if (SelectedSettingsSection is null || Settings is null || !Settings.HasUnsavedChanges)
        {
            return false;
        }

        revertNow();
        _ = ConfirmDiscardAndNavigateAsync(reapply);
        return true;
    }

    /// <summary>
    /// Names what will be discarded (SRS UI-05) through the same P3-T11 <see cref="IDialogService"/>
    /// shell every other back-office confirmation uses, then either discards the edit and completes
    /// the navigation <paramref name="reapply"/> describes, or does nothing at all - System stays
    /// selected, exactly as the operator left it, edit intact.
    /// </summary>
    private async Task ConfirmDiscardAndNavigateAsync(Action reapply)
    {
        if (_dialogService is null || Settings is null)
        {
            return;
        }

        var outcome = await _dialogService.ShowConfirmationAsync(
            headerText: "Discard unsaved settings changes?",
            message: "You have changes in Settings that have not been saved. Leaving now will "
                + "discard them.",
            confirmButtonText: "_Discard and leave").ConfigureAwait(true);

        if (outcome != DialogOutcome.Confirmed)
        {
            return;
        }

        Settings.RevertCommand.Execute(null);
        reapply();
    }

    // ---- Settings folded into the content pane (task P3-T19) --------------------------------------

    /// <summary>The nine System nav-rail items, for the rail's own buttons.</summary>
    public IReadOnlyList<string> SettingsSections { get; } = SettingsSectionNames;

    /// <summary>
    /// Which settings group the content pane shows, or null when the pane shows something else
    /// (Overview or a Catalogue section) instead. Replaces <c>SettingsWindow.axaml</c>'s old
    /// <c>TabControl</c>'s tab strip - the nav rail's nine System buttons each set this straight
    /// through <see cref="SelectSettingsSectionCommand"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsSectionActive))]
    [NotifyPropertyChangedFor(nameof(IsOverviewActive))]
    private string? _selectedSettingsSection;

    /// <summary>True once a settings group is showing in the content pane, false otherwise.</summary>
    public bool IsSettingsSectionActive => SelectedSettingsSection is not null;

    /// <summary>True only while the content pane shows Overview - neither Catalogue nor System.</summary>
    public bool IsOverviewActive => !IsCatalogueSectionActive && !IsSettingsSectionActive;

    /// <summary>
    /// The settings screen's own viewmodel, attached once by the composition root after
    /// construction (see <see cref="AttachSettings"/>), the same pattern <see cref="Catalogue"/>
    /// already uses and for the same reason: every test that builds this viewmodel directly from
    /// just an <see cref="ISession"/> keeps working unmodified. <see cref="SettingsViewModel"/>
    /// itself is untouched by task P3-T19 - only this hosting chrome changed.
    /// </summary>
    public SettingsViewModel? Settings { get; private set; }

    /// <summary>Wired once by the composition root, immediately after both singletons resolve.</summary>
    public void AttachSettings(SettingsViewModel settings, IDialogService dialogService)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dialogService);
        Settings = settings;
        _dialogService = dialogService;
    }

    /// <summary>
    /// Selects one of the nine System sub-items (task P3-T19). Never asks first - moving between
    /// System's own groups is not "leaving System"; it is the same one screen, one save
    /// <c>SettingsWindow.axaml</c>'s old <c>TabControl</c> always was.
    /// </summary>
    [RelayCommand]
    public void SelectSettingsSection(string section)
    {
        ArgumentNullException.ThrowIfNull(section);
        SelectedSettingsSection = section;
    }

    /// <summary>
    /// Loads the settings in force every time a System sub-item is selected coming from outside
    /// System - i.e. every genuine re-entry - but not when the selection merely moves between
    /// System's own nine sub-items while already there. The same "defer loading until navigated
    /// to, then re-read on every real visit" rule <see cref="OnSelectedCatalogueSectionChanged"/>
    /// already applies to Catalogue (see its own remarks), and the same rule
    /// <c>SettingsViewModel</c>'s own class remarks name directly: "re-read on open, never cached".
    /// </summary>
    partial void OnSelectedSettingsSectionChanged(string? oldValue, string? newValue)
    {
        if (newValue is not null && SelectedCatalogueSection is not null)
        {
            SelectedCatalogueSection = null;
        }

        if (newValue is not null && oldValue is null && Settings is not null)
        {
            Settings.LoadCommand.Execute(null);
        }
    }

    /// <summary>
    /// The window-level Ctrl+S KeyBinding's own target (task P3-T19, SRS UI-01). Deliberately
    /// re-scoped here, rather than left as a <c>SettingsWindow.axaml</c>-style KeyBinding on
    /// <see cref="SettingsViewModel.SaveCommand"/> directly, so it does nothing at all - not even
    /// save an unrelated group's own in-progress edit - whenever a nav group other than System is
    /// the one currently showing.
    /// </summary>
    [RelayCommand]
    public void SaveSystemSection()
    {
        if (IsSettingsSectionActive)
        {
            Settings?.SaveCommand.Execute(null);
        }
    }

    /// <summary>The window-level Ctrl+R KeyBinding's own target - see <see cref="SaveSystemSection"/>.</summary>
    [RelayCommand]
    public void RevertSystemSection()
    {
        if (IsSettingsSectionActive)
        {
            Settings?.RevertCommand.Execute(null);
        }
    }

    // ---- Status bar (SRS UI-09, UI-11: its own, distinct from the sales screen's) --------------

    public string StatusUserText => _session.CurrentUser is { } user
        ? user.DisplayName + " (" + (user.Role == Role.Owner ? "owner" : "cashier") + ")"
        : "Not signed in";

    public string StatusShiftText => _session.ShiftId is { } shiftId
        ? "Shift #" + shiftId.ToString(CultureInfo.InvariantCulture)
        : "No shift open";

    /// <summary>
    /// Whether the Overview's "Open shift" quick action has anything to do (task P3-T20 "Do this"
    /// #4: "Open shift where none is open") - the exact same <see cref="ISession.ShiftId"/> check
    /// <see cref="SalesViewModel.CanOpenShift"/> already makes, read from the one session
    /// singleton both viewmodels share (see this class's own remarks). Opening a shift itself
    /// still only happens on the sales screen, where the opening-float entry panel lives - this
    /// flag only decides whether the Overview button that leads there is worth showing.
    /// </summary>
    public bool CanOpenShift => _session.ShiftId is null;

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

    /// <summary>Raised when the owner asks for the label-printing screen.</summary>
    public event EventHandler? LabelPrintRequested;

    /// <summary>Raised when the owner asks for the purchase-order screen (SRS FR-4.5, FR-4.6, FR-4.10).</summary>
    public event EventHandler? PurchaseOrdersRequested;

    /// <summary>
    /// Raised by the Overview's "New sale" and "Open shift" quick actions (task P3-T20 "Do this"
    /// #4). Neither invents a new business action: both simply close this window and hand focus
    /// back to the one <c>SalesWindow</c> this back office was opened from, exactly the way it
    /// already sat there the whole time (see <c>App.axaml.cs</c>'s <c>ShowBackOffice</c>) - "New
    /// sale" leads to <c>SalesViewModel.NewSaleCommand</c> (its own F2), "Open shift" leads to
    /// <c>SalesViewModel.OpenShiftCommand</c> (its own opening-float panel), each already reachable
    /// from the sales screen the moment it is back in front.
    /// </summary>
    public event EventHandler? ReturnToSalesRequested;

    [RelayCommand]
    public void ManageUsers() => ManageUsersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void PrintLabels() => LabelPrintRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ManagePurchaseOrders() => PurchaseOrdersRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    public void ReturnToSales() => ReturnToSalesRequested?.Invoke(this, EventArgs.Empty);
}
