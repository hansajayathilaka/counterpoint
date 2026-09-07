using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.Views;

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
/// The sign-in screen comes first and the sales screen only exists after a password has verified
/// (SRS FR-1.1). Navigation lives here rather than in a viewmodel because opening a window is a
/// view concern; the viewmodels raise events and know nothing about windows.
/// </para>
/// </remarks>
// Fully qualified: "Application" on its own now binds to the Counterpoint.Application
// namespace rather than to Avalonia's base class.
public partial class App : Avalonia.Application
{
    private readonly LoginViewModel? _loginViewModel;
    private readonly SalesViewModel? _salesViewModel;
    private readonly UserAdminViewModel? _userAdminViewModel;

    private IClassicDesktopStyleApplicationLifetime? _desktop;

    /// <summary>For the XAML previewer, which has no container to resolve anything from.</summary>
    public App()
    {
    }

    /// <summary>The real entry point, called by the composition root.</summary>
    public App(
        LoginViewModel loginViewModel,
        SalesViewModel salesViewModel,
        UserAdminViewModel userAdminViewModel)
    {
        ArgumentNullException.ThrowIfNull(loginViewModel);
        ArgumentNullException.ThrowIfNull(salesViewModel);
        ArgumentNullException.ThrowIfNull(userAdminViewModel);

        _loginViewModel = loginViewModel;
        _salesViewModel = salesViewModel;
        _userAdminViewModel = userAdminViewModel;
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
            _loginViewModel.SignedIn += OnSignedIn;

            desktop.MainWindow = new LoginWindow { DataContext = _loginViewModel };

            // Asks the Application layer whether this database has a usable credential yet. Kicked
            // off rather than awaited: OnFrameworkInitializationCompleted is not async, and the
            // screen shows its own answer when it arrives.
            _loginViewModel.LoadCommand.Execute(null);
        }

        base.OnFrameworkInitializationCompleted();
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

        var login = desktop.MainWindow;

        // Shown before the sign-in window closes: with the default OnLastWindowClose shutdown
        // mode, closing the only window first would end the process.
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
}
