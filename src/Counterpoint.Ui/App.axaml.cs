using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Counterpoint.Application.Settings;
using Counterpoint.Ui.Styles;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.FirstRun;
using Counterpoint.Ui.ViewModels.Labels;
using Counterpoint.Ui.ViewModels.Purchasing;
using Counterpoint.Ui.ViewModels.Settings;
using Counterpoint.Ui.Views;
using Counterpoint.Ui.Views.Purchasing;
using Counterpoint.Ui.Views.Settings;

namespace Counterpoint.Ui;

/// <summary>
/// The Avalonia application object, and the one place that knows which window follows which.
/// </summary>
/// <remarks>
/// <para>
/// It is handed its viewmodels rather than constructing them: the composition root
/// (Counterpoint.App) owns every dependency, and Counterpoint.Ui cannot see the assemblies
/// those dependencies live in (CLAUDE.md "Project boundaries").
/// </para>
/// <para>
/// A database that has never been set up gets the first-run wizard first, and the sign-in screen
/// only once it is finished (SRS FR-10, FR-1.3). Otherwise the sign-in screen comes first and the
/// sales screen only exists after a password has verified (FR-1.1). Navigation lives here rather
/// than in a viewmodel because opening a window is a view concern; the viewmodels raise events
/// and know nothing about windows.
/// </para>
/// </remarks>
// Fully qualified: "Application" on its own now binds to the Counterpoint.Application
// namespace rather than to Avalonia's base class.
public partial class App : Avalonia.Application
{
    private readonly LoginViewModel? _loginViewModel;
    private readonly SalesViewModel? _salesViewModel;
    private readonly BackOfficeShellViewModel? _backOfficeShellViewModel;
    private readonly UserAdminViewModel? _userAdminViewModel;
    private readonly PurchaseOrderViewModel? _purchaseOrderViewModel;
    private readonly LabelPrintViewModel? _labelPrintViewModel;
    private readonly PrintQueueViewModel? _printQueueViewModel;
    private readonly SettingsViewModel? _settingsViewModel;
    private readonly RestoreWizardViewModel? _restoreWizardViewModel;
    private readonly FirstRunWizardViewModel? _firstRunViewModel;
    private readonly ISettings? _settings;
    private readonly AvaloniaThemeVariantSwitcher _themeVariantSwitcher = new();
    private readonly bool _firstRunRequired;

    private IClassicDesktopStyleApplicationLifetime? _desktop;

    /// <summary>For the XAML previewer, which has no container to resolve anything from.</summary>
    public App()
    {
    }

    /// <summary>The real entry point, called by the composition root.</summary>
    /// <param name="firstRunRequired">
    /// What <c>IFirstRunSetup.IsRequiredAsync()</c> answered at start-up, after the migrations and
    /// before any window opened. Asked there rather than here because
    /// <see cref="OnFrameworkInitializationCompleted"/> cannot await.
    /// </param>
    public App(
        LoginViewModel loginViewModel,
        SalesViewModel salesViewModel,
        BackOfficeShellViewModel backOfficeShellViewModel,
        UserAdminViewModel userAdminViewModel,
        PurchaseOrderViewModel purchaseOrderViewModel,
        LabelPrintViewModel labelPrintViewModel,
        PrintQueueViewModel printQueueViewModel,
        SettingsViewModel settingsViewModel,
        RestoreWizardViewModel restoreWizardViewModel,
        FirstRunWizardViewModel firstRunViewModel,
        ISettings settings,
        bool firstRunRequired)
    {
        ArgumentNullException.ThrowIfNull(loginViewModel);
        ArgumentNullException.ThrowIfNull(salesViewModel);
        ArgumentNullException.ThrowIfNull(backOfficeShellViewModel);
        ArgumentNullException.ThrowIfNull(userAdminViewModel);
        ArgumentNullException.ThrowIfNull(purchaseOrderViewModel);
        ArgumentNullException.ThrowIfNull(labelPrintViewModel);
        ArgumentNullException.ThrowIfNull(printQueueViewModel);
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        ArgumentNullException.ThrowIfNull(restoreWizardViewModel);
        ArgumentNullException.ThrowIfNull(firstRunViewModel);
        ArgumentNullException.ThrowIfNull(settings);

        _loginViewModel = loginViewModel;
        _salesViewModel = salesViewModel;
        _backOfficeShellViewModel = backOfficeShellViewModel;
        _userAdminViewModel = userAdminViewModel;
        _purchaseOrderViewModel = purchaseOrderViewModel;
        _labelPrintViewModel = labelPrintViewModel;
        _printQueueViewModel = printQueueViewModel;
        _settingsViewModel = settingsViewModel;
        _restoreWizardViewModel = restoreWizardViewModel;
        _firstRunViewModel = firstRunViewModel;
        _settings = settings;
        _firstRunRequired = firstRunRequired;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Before any window opens: SRS UI-13/NFR-U4, task P3-T10. ISettings.LoadAsync() has
        // already run in Counterpoint.App/Program.cs's PrepareDatabaseAsync by this point, so
        // ui.theme_variant is whatever the shop last chose, not SettingDefaults' own fallback.
        if (_settings is not null)
        {
            _themeVariantSwitcher.Apply(_settings.Current.Display.ThemeVariant);
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && _loginViewModel is not null)
        {
            _desktop = desktop;

            if (_firstRunRequired && _firstRunViewModel is not null)
            {
                _firstRunViewModel.Completed += OnFirstRunCompleted;

                desktop.MainWindow = new FirstRunWizardWindow { DataContext = _firstRunViewModel };
                _firstRunViewModel.LoadCommand.Execute(null);
            }
            else
            {
                ShowLogin();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Puts the sign-in screen up, and starts it asking whether the shop has a usable credential.
    /// </summary>
    private void ShowLogin()
    {
        if (_desktop is not { } desktop || _loginViewModel is null)
        {
            return;
        }

        _loginViewModel.SignedIn -= OnSignedIn;
        _loginViewModel.SignedIn += OnSignedIn;

        var login = new LoginWindow { DataContext = _loginViewModel };
        var previous = desktop.MainWindow;

        // Shown before the previous window closes: with the default OnLastWindowClose shutdown
        // mode, closing the only window first would end the process.
        desktop.MainWindow = login;

        if (previous is not null)
        {
            login.Show();
            previous.Close();
        }

        // Asks the Application layer whether this database has a usable credential yet. Kicked
        // off rather than awaited: OnFrameworkInitializationCompleted is not async, and the
        // screen shows its own answer when it arrives.
        _loginViewModel.LoadCommand.Execute(null);
    }

    /// <summary>The shop is configured and the owner has a password. Ask them to sign in.</summary>
    private void OnFirstRunCompleted(object? sender, EventArgs e)
    {
        if (_firstRunViewModel is not null)
        {
            _firstRunViewModel.Completed -= OnFirstRunCompleted;
        }

        ShowLogin();
    }

    /// <summary>
    /// Swaps the sign-in window for the sales screen, once and for all.
    /// </summary>
    private void OnSignedIn(object? sender, EventArgs e)
    {
        if (_desktop is not { } desktop || _salesViewModel is null)
        {
            return;
        }

        _loginViewModel!.SignedIn -= OnSignedIn;

        var sales = new SalesWindow { DataContext = _salesViewModel };
        _salesViewModel.BackOfficeRequested += (_, _) => ShowBackOffice(sales);
        _salesViewModel.PrintQueueRequested += (_, _) => ShowPrintQueue(sales);

        var login = desktop.MainWindow;

        desktop.MainWindow = sales;
        sales.Show();
        login?.Close();
    }

    /// <summary>
    /// Opens the back office (tasks P3-T13/P3-T18, SRS UI-11, UI-16): its own window, its own
    /// status bar, its own accent, its persistent nav rail navigating to Catalogue (folded straight
    /// into this window's own content pane - see <c>NavRail</c>/<c>CatalogueSectionContent</c>),
    /// Settings, Users, Purchasing and Labels (still their own windows, unchanged) - the same
    /// single process, single <c>IHost</c>, single database this whole application is (see
    /// <c>BackOfficeShellViewModel</c>'s remarks). Non-modal, like every other screen this class
    /// opens: a back-office window never blocks <c>SalesWindow</c>.
    /// </summary>
    private void ShowBackOffice(Window owner)
    {
        if (_backOfficeShellViewModel is null)
        {
            return;
        }

        var window = new BackOfficeShellWindow { DataContext = _backOfficeShellViewModel };

        // -= before += : BackOfficeShellViewModel is a singleton, so re-opening this window
        // without unsubscribing first would fire ShowUsers (and the rest) once per window ever
        // opened, each pointed at whichever window happened to be current at the time.
        _backOfficeShellViewModel.ManageUsersRequested -= OnManageUsersRequested;
        _backOfficeShellViewModel.ManageUsersRequested += OnManageUsersRequested;
        _backOfficeShellViewModel.PurchaseOrdersRequested -= OnPurchaseOrdersRequested;
        _backOfficeShellViewModel.PurchaseOrdersRequested += OnPurchaseOrdersRequested;
        _backOfficeShellViewModel.LabelPrintRequested -= OnLabelPrintRequested;
        _backOfficeShellViewModel.LabelPrintRequested += OnLabelPrintRequested;
        _backOfficeShellViewModel.SettingsRequested -= OnSettingsRequested;
        _backOfficeShellViewModel.SettingsRequested += OnSettingsRequested;

        // Task P3-T20: the Overview's "New sale" and "Open shift" quick actions both just want
        // this window gone, so SalesWindow (already sitting behind it as owner) is back in front
        // with its own F2/"Open shift" already there to press.
        _backOfficeShellViewModel.ReturnToSalesRequested -= OnReturnToSalesRequested;
        _backOfficeShellViewModel.ReturnToSalesRequested += OnReturnToSalesRequested;

        // Bugfix (task P3-T18 review): BackOfficeShellViewModel is a singleton that outlives this
        // window, so closing it must count as leaving Catalogue the same way SelectOverview does -
        // otherwise a section left selected when the owner closes the back office would still read
        // as "already there" on the next open, and OnSelectedCatalogueSectionChanged's re-entry
        // check (oldValue null) would never see the transition, silently skipping the reload that
        // picking a section again is supposed to trigger. window is a fresh instance per call, so
        // no -= is needed here the way the singleton VM's own events need it above.
        window.Closed += (_, _) => _backOfficeShellViewModel.SelectOverview();

        // Task P3-T20: Overview is the section the shell opens on, so its dashboard is loaded
        // unconditionally on every open - the same "refresh on open" ShowUsers/ShowPurchaseOrders/
        // ShowPrintQueue already do for their own screens below, not something
        // OnSelectedCatalogueSectionChanged's re-entry check would otherwise catch when the shell
        // was already showing Overview the last time it closed.
        _backOfficeShellViewModel.Dashboard?.LoadCommand.Execute(null);

        window.Show(owner);

        void OnManageUsersRequested(object? sender, EventArgs e) => ShowUsers(window);
        void OnPurchaseOrdersRequested(object? sender, EventArgs e) => ShowPurchaseOrders(window);
        void OnLabelPrintRequested(object? sender, EventArgs e) => ShowLabelPrint(window);
        void OnSettingsRequested(object? sender, EventArgs e) => ShowSettings(window);
        void OnReturnToSalesRequested(object? sender, EventArgs e) => window.Close();
    }

    private void ShowUsers(Window owner)
    {
        if (_userAdminViewModel is null)
        {
            return;
        }

        var window = new UserAdminWindow { DataContext = _userAdminViewModel };
        _userAdminViewModel.RefreshCommand.Execute(null);
        window.Show(owner);
    }

    /// <summary>
    /// Opens the purchase-order screen (SRS FR-4.5, FR-4.6, FR-4.10).
    /// </summary>
    private void ShowPurchaseOrders(Window owner)
    {
        if (_purchaseOrderViewModel is null)
        {
            return;
        }

        var window = new PurchaseOrderWindow { DataContext = _purchaseOrderViewModel };
        _purchaseOrderViewModel.RefreshCommand.Execute(null);
        window.Show(owner);
    }

    /// <summary>
    /// Opens the label-printing screen (SRS FR-2.10, FR-2.12).
    /// </summary>
    private void ShowLabelPrint(Window owner)
    {
        if (_labelPrintViewModel is null)
        {
            return;
        }

        var window = new LabelPrintWindow { DataContext = _labelPrintViewModel };
        window.Show(owner);
    }

    /// <summary>
    /// Opens the print queue screen (P1-T11): pending and failed jobs, with a retry button.
    /// </summary>
    private void ShowPrintQueue(Window owner)
    {
        if (_printQueueViewModel is null)
        {
            return;
        }

        var window = new PrintQueueWindow { DataContext = _printQueueViewModel };
        _printQueueViewModel.RefreshCommand.Execute(null);
        window.Show(owner);
    }

    /// <summary>
    /// Opens the settings screen, re-reading the settings in force as it does. Re-read on open,
    /// never cached from start-up - that is the risk P1-T03 names by name.
    /// </summary>
    private void ShowSettings(Window owner)
    {
        if (_settingsViewModel is null)
        {
            return;
        }

        var window = new SettingsWindow { DataContext = _settingsViewModel };
        _settingsViewModel.RestoreWizardRequested -= OnRestoreWizardRequested;
        _settingsViewModel.RestoreWizardRequested += OnRestoreWizardRequested;
        _settingsViewModel.LoadCommand.Execute(null);
        window.Show(owner);

        void OnRestoreWizardRequested(object? sender, EventArgs e) => ShowRestoreWizard(window);
    }

    /// <summary>
    /// Opens the guided restore wizard (SRS FR-11.12), owned by the settings window it was asked
    /// for from.
    /// </summary>
    private void ShowRestoreWizard(Window owner)
    {
        if (_restoreWizardViewModel is null)
        {
            return;
        }

        _restoreWizardViewModel.Reset();

        var window = new RestoreWizardWindow { DataContext = _restoreWizardViewModel };

        // A restore stages its result rather than touching the live database (see
        // PendingRestoreLocation's own remarks) - the till has to be closed and started again for
        // it to take effect, which only the desktop lifetime, not a viewmodel, may decide to do.
        void OnRestartRequested(object? sender, EventArgs e)
        {
            _restoreWizardViewModel.RestartRequested -= OnRestartRequested;
            _desktop?.Shutdown();
        }

        _restoreWizardViewModel.RestartRequested += OnRestartRequested;
        window.Closed += (_, _) => _restoreWizardViewModel.RestartRequested -= OnRestartRequested;

        window.Show(owner);
    }
}
