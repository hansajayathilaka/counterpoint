using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
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

    /// <summary>The username <see cref="FirstRunSeeder"/> gives the shop's owner account.</summary>
    internal static string SeededOwnerUsername => "owner";

    /// <summary>
    /// The password <see cref="SignInAsSeededOwnerAsync"/> gives that account. The seeder leaves
    /// an unusable hash behind on purpose, so a test that needs to be signed in has to set one
    /// the way the shop does - through <see cref="IInitialOwnerSetup"/>.
    /// </summary>
    internal static string SeededOwnerPassword => "till2026";

    /// <summary>
    /// Cheap Argon2id settings, used by every test that is not measuring the cost.
    /// </summary>
    /// <remarks>
    /// A test that creates a user and signs in twice would otherwise pay for six 64 MB
    /// derivations, and a suite that is slow gets run less often. The one test that must feel
    /// the real thing - the login budget - asks for
    /// <see cref="Argon2Parameters.Default"/> explicitly, and
    /// <c>PasswordHasherTests</c> proves that a hash made under one set of work factors still
    /// verifies under another, which is what makes this substitution honest.
    /// </remarks>
    internal static Argon2Parameters TestArgon2Parameters { get; } =
        new(MemoryKib: 256, Iterations: 1, Parallelism: 1);

    /// <summary>
    /// Builds a migrated, seeded database with the whole application wired over it.
    /// </summary>
    /// <param name="printerFailureMode">
    /// Set to <see cref="PrinterFailureMode.FailEveryJob"/> to prove a sale completes with a
    /// broken printer (AC-16 in miniature).
    /// </param>
    /// <param name="hashing">
    /// Argon2id work factors. Null uses <see cref="TestArgon2Parameters"/>; pass
    /// <see cref="Argon2Parameters.Default"/> to measure what the shop will actually feel.
    /// </param>
    internal static async Task<SaleFixture> CreateAsync(
        PrinterFailureMode printerFailureMode = PrinterFailureMode.None,
        Argon2Parameters? hashing = null)
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

        // The same lines as Counterpoint.App's CounterpointHostBuilderExtensions.
        services.AddSingleton<IScanItem, ScanItemHandler>();
        services.AddSingleton<CompleteSaleHandler>();
        services.AddSingleton<ICompleteSale>(p => p.GetRequiredService<CompleteSaleHandler>());
        services.AddSingleton<IQuoteSale>(p => p.GetRequiredService<CompleteSaleHandler>());

        // Security, wired exactly as the composition root wires it - in particular
        // IUserAdministration resolves only to the role-decorated instance, so a test cannot
        // accidentally prove AC-17 against an object the real application would never hand out.
        services.AddSingleton(hashing ?? TestArgon2Parameters);
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<Session>();
        services.AddSingleton<ISession>(p => p.GetRequiredService<Session>());
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IInitialOwnerSetup, InitialOwnerSetupService>();
        services.AddSingleton<IOwnerOverrideService, OwnerOverrideService>();
        services.AddSingleton<SecurityPolicyRecorder>();

        // The concrete service is built inside the factory rather than registered, so it is not
        // resolvable on its own: a test cannot get hold of an undecorated UserAdministrationService
        // any more than the running application can.
        services.AddSingleton<IUserAdministration>(p => RoleAuthorisation.Decorate<IUserAdministration>(
            ActivatorUtilities.CreateInstance<UserAdministrationService>(p),
            p.GetRequiredService<ISession>()));

        var provider = services.BuildServiceProvider();
        var fixture = new SaleFixture(root, provider);

        try
        {
            await provider.GetRequiredService<MigrationRunner>().ApplyPendingMigrationsAsync();
            await provider.GetRequiredService<FirstRunSeeder>().EnsureSeededAsync();
            await provider.GetRequiredService<SecurityPolicyRecorder>().EnsureRecordedAsync();

            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Builds the fixture and signs in as the seeded owner - who is also the user the seeded
    /// shift was opened by.
    /// </summary>
    /// <remarks>
    /// A bill is refused unless the signed-in user is the one it will be stamped with
    /// (SRS FR-1.1, FR-1.6), so every test that completes a sale has to be signed in as somebody,
    /// exactly as the shop is.
    /// </remarks>
    internal static async Task<SaleFixture> CreateSignedInAsync(
        PrinterFailureMode printerFailureMode = PrinterFailureMode.None)
    {
        var fixture = await CreateAsync(printerFailureMode);

        try
        {
            await fixture.SignInAsSeededOwnerAsync();
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

    /// <summary>
    /// The service if the container has a registration for it, or null if it has none - so that a
    /// test can assert that something is <em>not</em> resolvable.
    /// </summary>
    internal T? TryResolve<T>()
        where T : class => _services.GetService<T>();

    /// <summary>
    /// Gives the seeded owner its first password and signs in, through the same two Application
    /// services the login screen uses.
    /// </summary>
    internal async Task SignInAsSeededOwnerAsync()
    {
        await Resolve<IInitialOwnerSetup>().CompleteAsync(SeededOwnerUsername, SeededOwnerPassword);

        var result = await Resolve<IAuthenticationService>()
            .LogInAsync(SeededOwnerUsername, SeededOwnerPassword);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "The fixture could not sign in as the seeded owner: " + result.Message);
        }
    }

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
