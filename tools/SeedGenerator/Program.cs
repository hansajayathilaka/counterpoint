using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Pricing;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Devices.DependencyInjection;
using Counterpoint.Devices.Printing;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Counterpoint.SeedGenerator;

/// <summary>
/// The command-line half of P1-T16's tooling: <c>tools/SeedGenerator</c>, invoked by
/// <c>bash scripts/seed.sh</c> (seeding) and by <c>/perf-gate</c> (verifying), and the process
/// AC-15's acceptance test spawns and kills (<c>--sell-loop</c>).
/// </summary>
/// <remarks>
/// Three modes, one binary, because all three need the same DI wiring over a real on-disk
/// SQLCipher database and none of them is worth its own project:
/// <list type="bullet">
/// <item><c>--skus N --lines N --output ROOT</c> - builds the 20,000-SKU/100,000-line dataset
/// (AC-18 baseline) that the seeded-database performance harness and <c>HW-T07</c> measure
/// against.</item>
/// <item><c>--verify ROOT</c> - the hash-chain verification command (CLAUDE.md invariant 6):
/// walks <c>sale</c> and <c>audit_log</c> and reports the first broken link, or that there is
/// none.</item>
/// <item><c>--sell-loop --db-root ROOT --count N</c> - completes up to <c>N</c> real sales
/// through <see cref="ICompleteSale"/> against <c>ROOT</c>, printing <c>READY</c> once signed in
/// and <c>SOLD &lt;bill_no&gt;</c> after every commit, so a parent process can kill it at an
/// arbitrary point and know exactly what had committed at the moment of the kill (AC-15).</item>
/// </list>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (HasFlag(args, "--verify", out var verifyRoot))
            {
                await RunVerifyAsync(verifyRoot).ConfigureAwait(false);
                return 0;
            }

            if (HasFlag(args, "--sell-loop"))
            {
                var root = RequireOption(args, "--db-root");
                var count = int.Parse(RequireOption(args, "--count"), CultureInfo.InvariantCulture);
                return await RunSellLoopAsync(root, count).ConfigureAwait(false);
            }

            var skus = int.Parse(OptionOrDefault(args, "--skus", "20000"), CultureInfo.InvariantCulture);
            var lines = int.Parse(OptionOrDefault(args, "--lines", "100000"), CultureInfo.InvariantCulture);
            var output = RequireOption(args, "--output");

            await RunSeedAsync(skus, lines, output).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync("ERROR " + ex).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>Builds the catalogue and historical bills under <c>output/db/counterpoint.db</c>.</summary>
    private static async Task RunSeedAsync(int skus, int lines, string output)
    {
        Directory.CreateDirectory(output);
        var root = Path.GetFullPath(output);

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddCounterpointInfrastructure(root);
        services.AddLogging();

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<MigrationRunner>().ApplyPendingMigrationsAsync().ConfigureAwait(false);
        await provider.GetRequiredService<FirstRunSeeder>().EnsureSeededAsync().ConfigureAwait(false);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"Seeding {skus} SKUs and {lines} historical bill lines into {root}..."));

        await PerformanceDatasetSeeder.SeedAsync(
            provider.GetRequiredService<SqliteUnitOfWork>(),
            provider.GetRequiredService<TimeProvider>(),
            skus,
            lines).ConfigureAwait(false);

        Console.WriteLine("Seed complete: " + Path.Combine(root, "db", "counterpoint.db"));
    }

    /// <summary>Runs the hash-chain verification command over an already-seeded root.</summary>
    private static async Task RunVerifyAsync(string root)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddCounterpointInfrastructure(Path.GetFullPath(root));
        services.AddLogging();

        await using var provider = services.BuildServiceProvider();
        var connectionFactory = provider.GetRequiredService<IPosConnectionFactory>();

        var saleChain = await HashChainVerifier.VerifySaleChainAsync(connectionFactory).ConfigureAwait(false);
        var auditChain = await HashChainVerifier.VerifyAuditLogChainAsync(connectionFactory).ConfigureAwait(false);

        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"sale chain: {saleChain.RowsChecked} rows, {(saleChain.IsIntact ? "OK" : "BROKEN at id " + saleChain.FirstBrokenRowId)}"));
        Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"audit_log chain: {auditChain.RowsChecked} rows, {(auditChain.IsIntact ? "OK" : "BROKEN at id " + auditChain.FirstBrokenRowId)}"));

        if (!saleChain.IsIntact || !auditChain.IsIntact)
        {
            throw new InvalidOperationException("Hash chain verification failed.");
        }
    }

    /// <summary>
    /// AC-15's other half: completes up to <paramref name="count"/> sales against the seeded
    /// owner/shift/product at <paramref name="root"/>, one <c>ICompleteSale.CompleteAsync</c> call
    /// at a time, printing progress a parent process can watch for and then kill against.
    /// </summary>
    private static async Task<int> RunSellLoopAsync(string root, int count)
    {
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddCounterpointInfrastructure(root);
        services.AddCounterpointDevices(new FileReceiptPrinterOptions
        {
            OutputDirectory = Path.Combine(root, "receipts"),
        });
        services.AddLogging();

        services.AddSingleton(Argon2Parameters.Default);
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<Session>();
        services.AddSingleton<ISession>(p => p.GetRequiredService<Session>());
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IInitialOwnerSetup, InitialOwnerSetupService>();

        // The rest of what CompleteSaleHandler needs, wired the same shape
        // tests/Counterpoint.Integration.Tests/Sales/SaleFixture.cs uses, minus the role
        // decoration a signed-in owner never needs to get past anyway.
        services.AddSingleton<IBackupPassphraseStore>(p => p.GetRequiredService<BackupPassphraseStore>());
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettings>(p => p.GetRequiredService<SettingsService>());
        services.AddSingleton<IRoundingPolicy, SettingsRoundingPolicy>();
        services.AddSingleton<IDiscountAuthorisationService, DiscountAuthorisationService>();

        services.AddSingleton<CompleteSaleHandler>();
        services.AddSingleton<ICompleteSale>(p => p.GetRequiredService<CompleteSaleHandler>());

        await using var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<MigrationRunner>().ApplyPendingMigrationsAsync().ConfigureAwait(false);
        await provider.GetRequiredService<FirstRunSeeder>().EnsureSeededAsync().ConfigureAwait(false);
        await provider.GetRequiredService<ISettings>().LoadAsync().ConfigureAwait(false);

        const string ownerUsername = "owner";
        const string ownerPassword = "till2026-kill-harness";

        var ownerSetup = provider.GetRequiredService<IInitialOwnerSetup>();
        if (await ownerSetup.IsRequiredAsync().ConfigureAwait(false))
        {
            await ownerSetup.CompleteAsync(ownerUsername, ownerPassword).ConfigureAwait(false);
        }

        var authentication = provider.GetRequiredService<IAuthenticationService>();
        var signedIn = await authentication.LogInAsync(ownerUsername, ownerPassword).ConfigureAwait(false);

        if (!signedIn.Succeeded)
        {
            await Console.Error.WriteLineAsync("ERROR could not sign in: " + signedIn.Message).ConfigureAwait(false);
            return 1;
        }

        var connectionFactory = provider.GetRequiredService<IPosConnectionFactory>();
        var (shiftId, userId, variantId) = await OpenShiftAndVariantAsync(connectionFactory).ConfigureAwait(false);

        var completeSale = provider.GetRequiredService<ICompleteSale>();

        Console.WriteLine("READY");
        Console.Out.Flush();

        for (var i = 0; i < count; i++)
        {
            // A generous fixed cash tender: the exact bill total is not known until the handler
            // prices the line, and TenderCalculator lets a cash tender run over into change
            // rather than requiring the caller to have priced it first (SRS FR-3.24-FR-3.26).
            var completed = await completeSale.CompleteAsync(new CompleteSaleCommand(
                userId,
                shiftId,
                TimeProvider.System.GetLocalNow(),
                [new SaleLineRequest(variantId, 1m)],
                [new TenderRequest(TenderTypes.Cash, Money.FromDecimal(100_000m))])).ConfigureAwait(false);

            Console.WriteLine("SOLD " + completed.BillNo);
            Console.Out.Flush();
        }

        Console.WriteLine("DONE");
        return 0;
    }

    private static async Task<(long ShiftId, long UserId, long VariantId)> OpenShiftAndVariantAsync(
        IPosConnectionFactory connectionFactory)
    {
        var connection = await connectionFactory.OpenReadConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            await using var shiftCommand = connection.CreateCommand();
            shiftCommand.CommandText = "SELECT id, user_id FROM shift WHERE status = 'OPEN' ORDER BY id LIMIT 1;";
            await using var shiftReader = await shiftCommand.ExecuteReaderAsync().ConfigureAwait(false);

            if (!await shiftReader.ReadAsync().ConfigureAwait(false))
            {
                throw new InvalidOperationException("No open shift found. FirstRunSeeder should have opened one.");
            }

            var shiftId = shiftReader.GetInt64(0);
            var userId = shiftReader.GetInt64(1);
            await shiftReader.DisposeAsync().ConfigureAwait(false);

            await using var variantCommand = connection.CreateCommand();
            variantCommand.CommandText = "SELECT id FROM product_variant ORDER BY id LIMIT 1;";
            var variantId = (long)(await variantCommand.ExecuteScalarAsync().ConfigureAwait(false))!;

            return (shiftId, userId, variantId);
        }
    }

    private static bool HasFlag(string[] args, string flag, out string value)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.Ordinal) && i + 1 < args.Length)
            {
                value = args[i + 1];
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (var arg in args)
        {
            if (string.Equals(arg, flag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string RequireOption(string[] args, string flag) =>
        HasFlag(args, flag, out var value)
            ? value
            : throw new ArgumentException("Missing required option " + flag);

    private static string OptionOrDefault(string[] args, string flag, string fallback) =>
        HasFlag(args, flag, out var value) ? value : fallback;
}
