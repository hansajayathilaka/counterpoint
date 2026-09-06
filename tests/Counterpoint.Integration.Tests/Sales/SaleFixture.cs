using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Counterpoint.Application.Sales;
using Counterpoint.Devices.DependencyInjection;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Services;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Counterpoint.Integration.Tests.Sales;

/// <summary>
/// The whole application, minus the window, over a throwaway encrypted database.
/// </summary>
/// <remarks>
/// <para>
/// It composes the same container <c>Counterpoint.App</c> does - the same
/// <c>AddCounterpointInfrastructure</c>, the same <c>AddCounterpointDevices</c>, the same three
/// use-case registrations - so a sale in a test travels through exactly the wiring a sale at
/// the counter travels through. Only Avalonia is missing, and only because a window cannot be
/// opened in CI.
/// </para>
/// <para>
/// A real file through <see cref="PosConnectionFactory"/>, never the in-memory provider: the
/// append-only triggers, the foreign keys and the partial unique index on the open shift are
/// most of what these tests are about.
/// </para>
/// </remarks>
internal sealed class SaleFixture : IAsyncDisposable
{
    private readonly string _root;
    private readonly ServiceProvider _services;

    private SaleFixture(string root, ServiceProvider services)
    {
        _root = root;
        _services = services;
        ReceiptDirectory = Path.Combine(root, "receipts");
    }

    /// <summary>Where <see cref="FileReceiptPrinter"/> drops the rendered byte streams.</summary>
    internal string ReceiptDirectory { get; }

    /// <summary>
    /// Builds a migrated, seeded database with the whole application wired over it.
    /// </summary>
    /// <param name="printerFailureMode">
    /// Set to <see cref="PrinterFailureMode.FailEveryJob"/> to prove a sale completes with a
    /// broken printer (AC-16 in miniature).
    /// </param>
    internal static async Task<SaleFixture> CreateAsync(
        PrinterFailureMode printerFailureMode = PrinterFailureMode.None)
    {
        var root = Path.Combine(Path.GetTempPath(), "counterpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var clock = new FixedTimeProvider(
            new DateTimeOffset(2026, 9, 6, 9, 15, 0, TimeSpan.FromHours(5.5)));

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCounterpointInfrastructure(root);
        services.AddCounterpointDevices(
            new FileReceiptPrinterOptions
            {
                OutputDirectory = Path.Combine(root, "receipts"),
                FailureMode = printerFailureMode,
                TimeProvider = clock,
            },
            new PrintWorkerOptions { PollInterval = TimeSpan.FromMilliseconds(5), MaxAttempts = 3 });

        services.AddLogging();
        services.AddSingleton<IRoundingPolicy>(new HalfAwayFromZeroRounding(decimalPlaces: 2));

        // The same three lines as Counterpoint.App's CounterpointHostBuilderExtensions.
        services.AddSingleton<IScanItem, ScanItemHandler>();
        services.AddSingleton<CompleteSaleHandler>();
        services.AddSingleton<ICompleteSale>(p => p.GetRequiredService<CompleteSaleHandler>());
        services.AddSingleton<IQuoteSale>(p => p.GetRequiredService<CompleteSaleHandler>());

        var provider = services.BuildServiceProvider();
        var fixture = new SaleFixture(root, provider);

        try
        {
            await provider.GetRequiredService<MigrationRunner>().ApplyPendingMigrationsAsync();
            await provider.GetRequiredService<FirstRunSeeder>().EnsureSeededAsync();

            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    internal T Resolve<T>()
        where T : notnull => _services.GetRequiredService<T>();

    /// <summary>Opens a plain read connection so a test can assert against the raw rows.</summary>
    internal Task<DbConnection> OpenReadConnectionAsync() =>
        _services.GetRequiredService<IPosConnectionFactory>().OpenReadConnectionAsync();

    /// <summary>Runs a scalar query and returns it as invariant-culture text, or null.</summary>
    internal async Task<string?> ScalarAsync(string sql)
    {
        var connection = await OpenReadConnectionAsync();
        await using (connection.ConfigureAwait(false))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var value = await command.ExecuteScalarAsync();

            return value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    internal async Task<long> CountAsync(string sql) =>
        Convert.ToInt64(await ScalarAsync(sql), CultureInfo.InvariantCulture);

    public async ValueTask DisposeAsync()
    {
        await _services.DisposeAsync();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (IOException)
        {
            // A stray temp folder is not worth failing a green test run over.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
