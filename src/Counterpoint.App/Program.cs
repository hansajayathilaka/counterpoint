using System;
using System.Threading.Tasks;
using Avalonia;
using Counterpoint.App.DependencyInjection;
using Counterpoint.Application.Security;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Counterpoint.App;

/// <summary>
/// The composition root. Everything the application is made of is assembled here and nowhere
/// else (SAD §5).
/// </summary>
internal static class Program
{
    /// <summary>
    /// Refuses to be a second till, brings the database up to date, seeds a first run, starts the
    /// background workers, and only then opens the window.
    /// </summary>
    /// <remarks>
    /// The order is the point. The single-instance lock comes before anything opens the database,
    /// because two writers on one SQLite file is the failure this whole design exists to rule out
    /// (C-01). Migrating before the host starts means the print worker never polls a table that
    /// does not exist yet, and migrating before the window opens means a cashier is never looking
    /// at a sales screen backed by a schema this build cannot use (SRS NFR-M3, SAD §11).
    /// </remarks>
    [STAThread]
    public static int Main(string[] args)
    {
        // Before the host, before the connection factory, before anything touches the file.
        using var singleInstance = SingleInstanceLock.TryAcquire();
        if (singleInstance is null)
        {
            Console.Error.WriteLine(SingleInstanceLock.AlreadyRunningMessage);
            return 4;
        }

        try
        {
            using var host = Host.CreateApplicationBuilder(args)
                .ConfigureCounterpoint()
                .Build();

            PrepareDatabaseAsync(host.Services).GetAwaiter().GetResult();

            host.Start();
            try
            {
                BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(args);
            }
            finally
            {
                host.StopAsync().GetAwaiter().GetResult();
            }

            return 0;
        }
        catch (InvalidDataDirectoryException exception)
        {
            // The data directory is a network path, a sync root, or otherwise unusable. Refusing
            // to start is the correct outcome; the message says why in plain language.
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (SchemaMigrationException exception)
        {
            // The schema could not be brought up to what this build expects, so the till must
            // not trade against it. The message names the pre-migration backup.
            Console.Error.WriteLine(exception.Message);
            return 3;
        }
    }

    /// <summary>
    /// Avalonia configuration for the XAML previewer, which has no container. Do not remove.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Ui.App>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>Avalonia configuration for the real application, with its viewmodels injected.</summary>
    private static AppBuilder BuildAvaloniaApp(IServiceProvider services) =>
        AppBuilder.Configure(() => new Ui.App(
                services.GetRequiredService<Ui.ViewModels.LoginViewModel>(),
                services.GetRequiredService<Ui.ViewModels.SalesViewModel>(),
                services.GetRequiredService<Ui.ViewModels.UserAdminViewModel>()))
            .UsePlatformDetect()
            .LogToTrace();

    private static async Task PrepareDatabaseAsync(IServiceProvider services)
    {
        await services.GetRequiredService<MigrationRunner>()
            .ApplyPendingMigrationsAsync()
            .ConfigureAwait(false);

        await services.GetRequiredService<FirstRunSeeder>()
            .EnsureSeededAsync()
            .ConfigureAwait(false);

        // What this build hashes with, written down where the owner can see it. P1-T03's settings
        // framework takes this over.
        await services.GetRequiredService<SecurityPolicyRecorder>()
            .EnsureRecordedAsync()
            .ConfigureAwait(false);
    }
}
