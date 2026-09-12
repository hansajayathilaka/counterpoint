using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Catalogue;
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
    private readonly UserAdminViewModel? _userAdminViewModel;
    private readonly CatalogueViewModel? _catalogueViewModel;
    private readonly PurchaseOrderViewModel? _purchaseOrderViewModel;
    private readonly LabelPrintViewModel? _labelPrintViewModel;
    private readonly PrintQueueViewModel? _printQueueViewModel;
    private readonly SettingsViewModel? _settingsViewModel;
    private readonly RestoreWizardViewModel? _restoreWizardViewModel;
    private readonly FirstRunWizardViewModel? _firstRunViewModel;
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
        UserAdminViewModel userAdminViewModel,
        CatalogueViewModel catalogueViewModel,
        PurchaseOrderViewModel purchaseOrderViewModel,
        LabelPrintViewModel labelPrintViewModel,
        PrintQueueViewModel printQueueViewModel,
        SettingsViewModel settingsViewModel,
        RestoreWizardViewModel restoreWizardViewModel,
        FirstRunWizardViewModel firstRunViewModel,
        bool firstRunRequired)
    {
        ArgumentNullException.ThrowIfNull(loginViewModel);
        ArgumentNullException.ThrowIfNull(salesViewModel);
        ArgumentNullException.ThrowIfNull(userAdminViewModel);
        ArgumentNullException.ThrowIfNull(catalogueViewModel);
        ArgumentNullException.ThrowIfNull(purchaseOrderViewModel);
        ArgumentNullException.ThrowIfNull(labelPrintViewModel);
        ArgumentNullException.ThrowIfNull(printQueueViewModel);
        ArgumentNullException.ThrowIfNull(settingsViewModel);
        ArgumentNullException.ThrowIfNull(restoreWizardViewModel);
        ArgumentNullException.ThrowIfNull(firstRunViewModel);

        _loginViewModel = loginViewModel;
        _salesViewModel = salesViewModel;
        _userAdminViewModel = userAdminViewModel;
        _catalogueViewModel = catalogueViewModel;
        _purchaseOrderViewModel = purchaseOrderViewModel;
        _labelPrintViewModel = labelPrintViewModel;
        _printQueueViewModel = printQueueViewModel;
        _settingsViewModel = settingsViewModel;
        _restoreWizardViewModel = restoreWizardViewModel;
        _firstRunViewModel = firstRunViewModel;
        _firstRunRequired = firstRunRequired;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
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
        _salesViewModel.ManageUsersRequested += (_, _) => ShowUsers(sales);
        _salesViewModel.CatalogueRequested += (_, _) => ShowCatalogue(sales);
        _salesViewModel.PurchaseOrdersRequested += (_, _) => ShowPurchaseOrders(sales);
        _salesViewModel.LabelPrintRequested += (_, _) => ShowLabelPrint(sales);
        _salesViewModel.PrintQueueRequested += (_, _) => ShowPrintQueue(sales);
        _salesViewModel.SettingsRequested += (_, _) => ShowSettings(sales);

        var login = desktop.MainWindow;

        desktop.MainWindow = sales;
        sales.Show();
        login?.Close();
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
    /// Opens the catalogue reference-data screen: category, brand, unit, tax class, supplier,
    /// customer (SRS FR-2.20, FR-2.21, FR-6).
    /// </summary>
    private void ShowCatalogue(Window owner)
    {
        if (_catalogueViewModel is null)
        {
            return;
        }

        var window = new CatalogueWindow { DataContext = _catalogueViewModel };
        _catalogueViewModel.LoadCommand.Execute(null);
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
