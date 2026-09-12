using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Counterpoint.App.DependencyInjection;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Settings.FirstRun;
using Counterpoint.Backup.Restore;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Counterpoint.App;

/// <summary>
/// The composition root. Everything the application is made of is assembled here and nowhere
/// else (SAD §5).
/// </summary>
internal static partial class Program
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

            var firstRunRequired = PrepareDatabaseAsync(host.Services).GetAwaiter().GetResult();

            host.Start();
            try
            {
                BuildAvaloniaApp(host.Services, firstRunRequired).StartWithClassicDesktopLifetime(args);
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
    private static AppBuilder BuildAvaloniaApp(IServiceProvider services, bool firstRunRequired) =>
        AppBuilder.Configure(() => new Ui.App(
                services.GetRequiredService<Ui.ViewModels.LoginViewModel>(),
                services.GetRequiredService<Ui.ViewModels.SalesViewModel>(),
                services.GetRequiredService<Ui.ViewModels.UserAdminViewModel>(),
                services.GetRequiredService<Ui.ViewModels.Catalogue.CatalogueViewModel>(),
                services.GetRequiredService<Ui.ViewModels.Purchasing.PurchaseOrderViewModel>(),
                services.GetRequiredService<Ui.ViewModels.Labels.LabelPrintViewModel>(),
                services.GetRequiredService<Ui.ViewModels.PrintQueueViewModel>(),
                services.GetRequiredService<Ui.ViewModels.Settings.SettingsViewModel>(),
                services.GetRequiredService<Ui.ViewModels.Settings.RestoreWizardViewModel>(),
                services.GetRequiredService<Ui.ViewModels.FirstRun.FirstRunWizardViewModel>(),
                firstRunRequired))
            .UsePlatformDetect()
            .LogToTrace();

    /// <returns>
    /// True when this database has never been through the first-run wizard, and the wizard is
    /// therefore the first window the shop sees (SRS FR-10, P1-T03).
    /// </returns>
    private static async Task<bool> PrepareDatabaseAsync(IServiceProvider services)
    {
        // Before the migrator, before anything else touches the file: a guided restore (P1-T15,
        // SRS FR-11.12) stages the chosen backup rather than swapping the live database out from
        // under a running process (CLAUDE.md "single named-mutex instance"), and this is the
        // first moment after start-up that nothing has opened a connection to it yet.
        ApplyPendingRestoreIfPresent(services);

        await services.GetRequiredService<MigrationRunner>()
            .ApplyPendingMigrationsAsync()
            .ConfigureAwait(false);

        await services.GetRequiredService<FirstRunSeeder>()
            .EnsureSeededAsync()
            .ConfigureAwait(false);

        // What this build hashes with, written down where the owner can see it. Still written
        // rather than read: the settings framework owns the FR-10 keys, and deliberately not the
        // security.* ones (docs/01_DATA_MODEL.md §11).
        await services.GetRequiredService<SecurityPolicyRecorder>()
            .EnsureRecordedAsync()
            .ConfigureAwait(false);

        // Before anything reads a setting, and before the window opens: the rounding policy, the
        // receipt template and every later feature's limits all come from here (SRS FR-10,
        // NFR-M1). A change made afterwards republishes this cache without a restart.
        await services.GetRequiredService<ISettings>()
            .LoadAsync()
            .ConfigureAwait(false);

        // P1-T07's startup consistency check (SAD §3). Diagnostic only: it never blocks the
        // till from opening (CLAUDE.md invariant 7 in spirit - nothing here may hold up the
        // sales screen), and a mismatch is logged loudly rather than corrected on the spot,
        // because fixing it is IRebuildStockBalance's job, run deliberately.
        await RunStockConsistencyCheckAsync(services).ConfigureAwait(false);

        // Last, because it reads the same app_setting table the four steps above have just
        // finished with, and because the answer decides which window opens. The wizard writes
        // everything it collects in one transaction of its own; nothing here pre-empts it.
        return await services.GetRequiredService<IFirstRunSetup>()
            .IsRequiredAsync()
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Moves a guided restore's staged database into place, if a P1-T15 restore left one behind
    /// (SRS FR-11.12). No connection to <c>db/counterpoint.db</c> exists yet at this point in
    /// start-up, so a plain file move is safe: nothing holds a lock on either file, and there is
    /// no in-memory cache of the old database's contents anywhere in this process to invalidate.
    /// </summary>
    /// <remarks>
    /// Deletes the WAL and shared-memory sidecar files the database being replaced may have left
    /// behind first - they describe transactions against the <em>old</em> file's contents, and
    /// applying them against the restored one would corrupt it rather than the crash-recovery
    /// journal SQLite intends them to be.
    /// </remarks>
    private static void ApplyPendingRestoreIfPresent(IServiceProvider services)
    {
        var dataDirectory = PosDataDirectory.Resolve().EnsureCreated();
        var stagedPath = PendingRestoreLocation.StagingFilePath(dataDirectory.SnapshotDirectory);

        if (!File.Exists(stagedPath))
        {
            return;
        }

        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("GuidedRestore");

        var liveDatabasePath = dataDirectory.DatabaseFilePath;

        foreach (var sidecarSuffix in new[] { "-wal", "-shm" })
        {
            var sidecarPath = liveDatabasePath + sidecarSuffix;
            if (File.Exists(sidecarPath))
            {
                File.Delete(sidecarPath);
            }
        }

        File.Move(stagedPath, liveDatabasePath, overwrite: true);

        var stagingDirectory = Path.GetDirectoryName(stagedPath);
        if (stagingDirectory is not null && Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        PendingRestoreApplied(logger, liveDatabasePath);
    }

    [LoggerMessage(
        EventId = 7303,
        Level = LogLevel.Warning,
        Message = "A guided restore staged at the last shutdown has been applied to {DatabasePath}. "
            + "This till is now trading on the restored data.")]
    private static partial void PendingRestoreApplied(ILogger logger, string databasePath);

    /// <summary>
    /// Runs the P1-T07 consistency check and logs the outcome. Never throws: a diagnostic that
    /// could stop the till from opening would be worse than the drift it is looking for.
    /// </summary>
    private static async Task RunStockConsistencyCheckAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("StockConsistencyCheck");

        try
        {
            var report = await services.GetRequiredService<IStockConsistencyCheck>()
                .CheckAsync()
                .ConfigureAwait(false);

            if (!report.HasMismatch)
            {
                StockConsistencyCheckClean(logger, report.SampledCount);
            }

            // A mismatch is already logged as a Warning per variant by
            // IStockConsistencyCheck itself.
        }
#pragma warning disable CA1031 // Catch-all is the requirement here, not an oversight.
        catch (Exception exception)
        {
            // The check itself failing is not a reason to refuse the till - it is the same
            // "never block the sale" spirit as CLAUDE.md invariant 7, applied to a diagnostic
            // that has no business holding up the sales screen.
            StockConsistencyCheckFailed(logger, exception);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(
        EventId = 7301,
        Level = LogLevel.Information,
        Message = "Stock consistency check: {SampledCount} variants sampled, no mismatch.")]
    private static partial void StockConsistencyCheckClean(ILogger logger, int sampledCount);

    [LoggerMessage(
        EventId = 7302,
        Level = LogLevel.Warning,
        Message = "Stock consistency check could not run.")]
    private static partial void StockConsistencyCheckFailed(ILogger logger, Exception exception);
}
