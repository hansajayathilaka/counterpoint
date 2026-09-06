using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.Views;

namespace Counterpoint.Ui;

/// <summary>
/// The Avalonia application object.
/// </summary>
/// <remarks>
/// It is handed its root viewmodel rather than constructing one: the composition root
/// (Counterpoint.App) owns every dependency, and Counterpoint.Ui cannot see the assemblies
/// those dependencies live in (CLAUDE.md "Project boundaries").
/// </remarks>
// Fully qualified: "Application" on its own now binds to the Counterpoint.Application
// namespace rather than to Avalonia's base class.
public partial class App : Avalonia.Application
{
    private readonly SalesViewModel? _salesViewModel;

    /// <summary>For the XAML previewer, which has no container to resolve anything from.</summary>
    public App()
    {
    }

    /// <summary>The real entry point, called by the composition root.</summary>
    public App(SalesViewModel salesViewModel)
    {
        ArgumentNullException.ThrowIfNull(salesViewModel);
        _salesViewModel = salesViewModel;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new SalesWindow
            {
                DataContext = _salesViewModel,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
