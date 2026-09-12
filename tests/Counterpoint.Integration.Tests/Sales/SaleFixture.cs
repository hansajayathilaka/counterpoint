using System;
using System.Data.Common;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Import;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Labels;
using Counterpoint.Application.Pricing;
using Counterpoint.Application.Purchasing;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Settings.FirstRun;
using Counterpoint.Application.Shifts;
using Counterpoint.Backup.DependencyInjection;
using Counterpoint.Backup.Snapshots;
using Counterpoint.Devices.DependencyInjection;
using Counterpoint.Devices.Labels;
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

    private SaleFixture(string root, ServiceProvider services, string snapshotDirectory)
    {
        _root = root;
        _services = services;
        ReceiptDirectory = Path.Combine(root, "receipts");
        LabelDirectory = Path.Combine(root, "labels");
        SnapshotDirectory = snapshotDirectory;
    }

    /// <summary>
    /// The data directory root this fixture's whole container was built over - the same path a
    /// second, independent container could be pointed at to open the identical on-disk database
    /// (P1-T16's performance regression guard does exactly this to measure a cold start).
    /// </summary>
    internal string Root => _root;

    /// <summary>Where <see cref="FileReceiptPrinter"/> drops the rendered byte streams.</summary>
    internal string ReceiptDirectory { get; }

    /// <summary>Where <see cref="FileLabelPrinter"/> drops the rendered TSPL byte streams.</summary>
    internal string LabelDirectory { get; }

    /// <summary>
    /// Where <see cref="Counterpoint.Backup.Snapshots.SnapshotService"/> writes the encrypted
    /// backup file, when <c>includeBackup</c> was passed to <see cref="CreateAsync"/>. Same value
    /// <c>PosDataDirectory.SnapshotDirectory</c> resolves to for this fixture's own root (P0-T07).
    /// </summary>
    internal string SnapshotDirectory { get; }

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
    /// <param name="labelPrinterFailureMode">
    /// Set to <see cref="PrinterFailureMode.FailEveryJob"/> to prove a GRN's already-committed
    /// stock survives a broken label printer (P2-T07's own label-batch hook, the same AC-16
    /// shape as <paramref name="printerFailureMode"/> but for <see cref="FileLabelPrinter"/>
    /// rather than <see cref="FileReceiptPrinter"/>).
    /// </param>
    /// <param name="hashing">
    /// Argon2id work factors. Null uses <see cref="TestArgon2Parameters"/>; pass
    /// <see cref="Argon2Parameters.Default"/> to measure what the shop will actually feel.
    /// </param>
    /// <param name="includeBackup">
    /// Wires <c>Counterpoint.Backup</c>'s <c>SnapshotService</c> and <c>RestoreService</c> into
    /// the container, exactly as <c>CounterpointHostBuilderExtensions</c> does (P0-T07). Off by
    /// default: most sale tests need none of it, and Argon2id parameters are shared with sign-in
    /// through the same <see cref="Argon2Parameters"/> registration either way.
    /// </param>
    internal static async Task<SaleFixture> CreateAsync(
        PrinterFailureMode printerFailureMode = PrinterFailureMode.None,
        Argon2Parameters? hashing = null,
        bool includeBackup = false,
        PrinterFailureMode labelPrinterFailureMode = PrinterFailureMode.None)
    {
        var root = Path.Combine(Path.GetTempPath(), "counterpoint-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        // Resolved a second time, deliberately - the same pattern
        // CounterpointHostBuilderExtensions uses: AddCounterpointInfrastructure resolves and
        // registers its own PosDataDirectory internally and does not hand it back, and Resolve()
        // is idempotent for the same root, so this costs a redundant filesystem check, not a
        // second source of truth.
        var dataDirectory = PosDataDirectory.Resolve(root).EnsureCreated();

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
            new PrintWorkerOptions { PollInterval = TimeSpan.FromMilliseconds(5), MaxAttempts = 3 },
            labelPrinterOptions: new FileLabelPrinterOptions
            {
                OutputDirectory = Path.Combine(root, "labels"),
                FailureMode = labelPrinterFailureMode,
                TimeProvider = clock,
            });

        services.AddLogging();

        // The settings framework, wired as the composition root wires it. IRoundingPolicy is
        // built from the shop's own decimal places and rounding rule, so a test that changes
        // them changes what the next bill rounds to - without a restart (SRS FR-10.2).
        //
        // ISettings is registered decorated, exactly as IUserAdministration is and exactly as
        // CounterpointHostBuilderExtensions registers it: SaveAsync and UpdateAsync are owner-only
        // (SRS §3.3 ROLE-2, NFR-S2, AC-17), so a test must not be able to change a setting through
        // an object the real application would never hand out. The read side carries no attribute,
        // which is why LoadAsync below still works before anybody has signed in.
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettings>(p => RoleAuthorisation.Decorate<ISettings>(
            p.GetRequiredService<SettingsService>(),
            p.GetRequiredService<ISession>()));
        services.AddSingleton<IRoundingPolicy, SettingsRoundingPolicy>();
        services.AddSingleton<IFirstRunSetup, FirstRunSetupService>();

        // Same shape as ISettings and IUserAdministration: the concrete BackupPassphraseStore
        // is registered by AddCounterpointInfrastructure above; only the role-decorated
        // interface is handed out, exactly as the composition root wires it (SRS NFR-S2, AC-17).
        services.AddSingleton<IBackupPassphraseStore>(p => RoleAuthorisation.Decorate<IBackupPassphraseStore>(
            p.GetRequiredService<BackupPassphraseStore>(),
            p.GetRequiredService<ISession>()));

        // The same lines as Counterpoint.App's CounterpointHostBuilderExtensions.
        services.AddSingleton<IScanItem, ScanItemHandler>();
        services.AddSingleton<CompleteSaleHandler>();
        services.AddSingleton<ICompleteSale>(p => p.GetRequiredService<CompleteSaleHandler>());
        services.AddSingleton<IQuoteSale>(p => p.GetRequiredService<CompleteSaleHandler>());

        // P1-T10: bill cancellation (SRS FR-3.34), wired exactly as CounterpointHostBuilderExtensions
        // wires it - decorated-only, same shape as IUserAdministration above (NFR-S2, AC-17).
        services.AddSingleton<ICancelSale>(p => RoleAuthorisation.Decorate<ICancelSale>(
            ActivatorUtilities.CreateInstance<CancelSaleHandler>(p),
            p.GetRequiredService<ISession>()));

        // P1-T11: reprint (SRS FR-3.36, FR-7.5, FR-7.6) - undecorated, the same shape as
        // ICompleteSale above (any signed-in cashier may reprint, SRS §3.3 ROLE-1).
        services.AddSingleton<IReprintReceipt>(p => ActivatorUtilities.CreateInstance<ReprintReceiptHandler>(p));

        // P1-T07: the stock enquiry screen's use case, wired exactly as the composition root
        // wires it.
        services.AddSingleton<IStockEnquiry, StockEnquiryService>();

        // P1-T14: opening a shift (SRS FR-8.1) and the home-screen dashboard (SRS FR-9.7), wired
        // exactly as CounterpointHostBuilderExtensions wires them - IOpenShift undecorated,
        // through ActivatorUtilities because OpenShiftHandler's constructor takes the concrete
        // Session, not ISession.
        services.AddSingleton<IOpenShift>(p => ActivatorUtilities.CreateInstance<OpenShiftHandler>(p));
        services.AddSingleton<IDashboardQueries, DashboardService>();

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

        // P1-T04: category, brand, unit, tax class, supplier, customer maintenance - wired
        // exactly as the composition root wires them, decorated-only, same as IUserAdministration
        // above (SRS FR-2.20, FR-2.21, FR-6, NFR-S2, AC-17).
        services.AddSingleton<ICategoryMaintenance>(p => RoleAuthorisation.Decorate<ICategoryMaintenance>(
            ActivatorUtilities.CreateInstance<CategoryMaintenanceService>(p),
            p.GetRequiredService<ISession>()));
        services.AddSingleton<IBrandMaintenance>(p => RoleAuthorisation.Decorate<IBrandMaintenance>(
            ActivatorUtilities.CreateInstance<BrandMaintenanceService>(p),
            p.GetRequiredService<ISession>()));
        services.AddSingleton<IUomMaintenance>(p => RoleAuthorisation.Decorate<IUomMaintenance>(
            ActivatorUtilities.CreateInstance<UomMaintenanceService>(p),
            p.GetRequiredService<ISession>()));
        services.AddSingleton<ITaxClassMaintenance>(p => RoleAuthorisation.Decorate<ITaxClassMaintenance>(
            ActivatorUtilities.CreateInstance<TaxClassMaintenanceService>(p),
            p.GetRequiredService<ISession>()));
        services.AddSingleton<ISupplierMaintenance>(p => RoleAuthorisation.Decorate<ISupplierMaintenance>(
            ActivatorUtilities.CreateInstance<SupplierMaintenanceService>(p),
            p.GetRequiredService<ISession>()));
        services.AddSingleton<ICustomerMaintenance>(p => RoleAuthorisation.Decorate<ICustomerMaintenance>(
            ActivatorUtilities.CreateInstance<CustomerMaintenanceService>(p),
            p.GetRequiredService<ISession>()));

        // P1-T05: product, variant and UOM conversion maintenance - wired exactly as the six
        // above (SRS FR-2.1-FR-2.8, FR-3.6, AC-08, NFR-S2, AC-17).
        services.AddSingleton<IProductMaintenance>(p => RoleAuthorisation.Decorate<IProductMaintenance>(
            ActivatorUtilities.CreateInstance<ProductMaintenanceService>(p),
            p.GetRequiredService<ISession>()));

        // P2-T06: suppliers and purchase orders (SRS FR-4.5, FR-4.6, FR-4.10) - owner-only, wired
        // exactly as CounterpointHostBuilderExtensions wires it, decorated-only, same shape as
        // IProductMaintenance above (NFR-S2, AC-17).
        services.AddSingleton<IPurchaseOrderService>(p => RoleAuthorisation.Decorate<IPurchaseOrderService>(
            ActivatorUtilities.CreateInstance<PurchaseOrderService>(p),
            p.GetRequiredService<ISession>()));

        // P2-T07: goods receipt (SRS FR-4.7, FR-4.8, AC-08) - owner-only, wired exactly as
        // CounterpointHostBuilderExtensions wires it, decorated-only, same shape as
        // IPurchaseOrderService above (NFR-S2, AC-17).
        services.AddSingleton<IGoodsReceiptService>(p => RoleAuthorisation.Decorate<IGoodsReceiptService>(
            ActivatorUtilities.CreateInstance<GoodsReceiptService>(p),
            p.GetRequiredService<ISession>()));

        // P1-T08: pricing and discounts (SRS FR-2.13-FR-2.19, FR-3.7-FR-3.10, Q-12), the same
        // two lines as Counterpoint.App's CounterpointHostBuilderExtensions.
        // IDiscountAuthorisationService is not owner-only - a cashier applies a discount inside
        // the cap without needing anyone's role, and going over it is gated by OverrideToken, not
        // RequiresRoleAttribute - so it is registered plain, undecorated.
        services.AddSingleton<IDiscountAuthorisationService, DiscountAuthorisationService>();

        // P2-T01: the return policy engine (SRS FR-5, BR-*, FR-10.5, Q-03), the same line as
        // Counterpoint.App's CounterpointHostBuilderExtensions. Not owner-only either, for the
        // same reason IDiscountAuthorisationService above is not.
        services.AddSingleton<IReturnPolicyAuthorisationService, ReturnPolicyAuthorisationService>();

        // P2-T02: linked returns (SRS FR-5.1-FR-5.10, AC-03, AC-06), wired exactly as
        // Counterpoint.App's CounterpointHostBuilderExtensions - undecorated, the same shape as
        // IReturnPolicyAuthorisationService above.
        services.AddSingleton<ICreateReturn, CreateReturnHandler>();

        // P2-T03: unlinked returns (SRS FR-5.19, NFR-S2), wired exactly as
        // Counterpoint.App's CounterpointHostBuilderExtensions - undecorated, the same shape as
        // ICreateReturn above: the cashier still takes the return and stays signed in throughout
        // (SRS FR-1.7); what makes this path high-friction is that
        // CreateUnlinkedReturnCommand.Override is mandatory on every call, not that the method
        // needs anyone's role.
        services.AddSingleton<ICreateUnlinkedReturn, CreateUnlinkedReturnHandler>();

        // Bulk price update by category, brand or supplier (SRS FR-2.19) is owner only, wired
        // exactly as IProductMaintenance above.
        services.AddSingleton<IBulkPriceUpdateService>(p => RoleAuthorisation.Decorate<IBulkPriceUpdateService>(
            ActivatorUtilities.CreateInstance<BulkPriceUpdateService>(p),
            p.GetRequiredService<ISession>()));

        // P1-T13: spreadsheet catalogue import and export (SRS FR-2.22, FR-2.23) - wired exactly
        // as CounterpointHostBuilderExtensions wires it, decorated-only, same as
        // IBulkPriceUpdateService above (NFR-S2, AC-17).
        services.AddSingleton<ICatalogueImportService>(p => RoleAuthorisation.Decorate<ICatalogueImportService>(
            ActivatorUtilities.CreateInstance<CatalogueImportService>(p),
            p.GetRequiredService<ISession>()));

        // P1-T06: barcode administration - wired exactly as the composition root wires it
        // (SRS FR-2.9, FR-2.10, FR-2.24, NFR-S2, AC-17). IProductSearchService and
        // IReindexSearchCommand need no equivalent line: AddCounterpointInfrastructure above
        // already registers them undecorated, the same as IProductLookup.
        services.AddSingleton<IBarcodeMaintenance>(p => RoleAuthorisation.Decorate<IBarcodeMaintenance>(
            ActivatorUtilities.CreateInstance<BarcodeMaintenanceService>(p),
            p.GetRequiredService<ISession>()));

        // P1-T12: label printing (SRS FR-2.10, FR-2.12) - owner-only, wired exactly as
        // CounterpointHostBuilderExtensions wires it, decorated-only, same as the catalogue
        // maintenance interfaces above (NFR-S2, AC-17).
        services.AddSingleton<ILabelPrintService>(p => RoleAuthorisation.Decorate<ILabelPrintService>(
            ActivatorUtilities.CreateInstance<LabelPrintService>(p),
            p.GetRequiredService<ISession>()));

        // P0-T07: SnapshotService and RestoreService, wired exactly as
        // CounterpointHostBuilderExtensions wires them, and after the Argon2Parameters
        // registration above so AddCounterpointBackup's TryAddSingleton(Argon2Parameters.Default)
        // finds the cheap test one already there rather than racing it.
        if (includeBackup)
        {
            services.AddCounterpointBackup(new SnapshotOptions(dataDirectory.SnapshotDirectory));
        }

        var provider = services.BuildServiceProvider();
        var fixture = new SaleFixture(root, provider, dataDirectory.SnapshotDirectory);

        try
        {
            await provider.GetRequiredService<MigrationRunner>().ApplyPendingMigrationsAsync();
            await provider.GetRequiredService<FirstRunSeeder>().EnsureSeededAsync();
            await provider.GetRequiredService<SecurityPolicyRecorder>().EnsureRecordedAsync();
            await provider.GetRequiredService<ISettings>().LoadAsync();

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
        PrinterFailureMode printerFailureMode = PrinterFailureMode.None,
        bool includeBackup = false,
        PrinterFailureMode labelPrinterFailureMode = PrinterFailureMode.None)
    {
        var fixture = await CreateAsync(
            printerFailureMode,
            includeBackup: includeBackup,
            labelPrinterFailureMode: labelPrinterFailureMode);

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

    /// <summary>
    /// Runs a write statement on the single write connection, outside any Application-layer
    /// handler - for reaching a state only a raw repair-session UPDATE can (P1-T14: closing the
    /// seeded shift directly, since P3-T01's shift-close handler does not exist yet, is exactly
    /// the column-scoped update <c>trg_shift_restricted_update</c> and
    /// <c>trg_shift_close_fields_together</c> already permit).
    /// </summary>
    internal async Task ExecuteAsync(string sql)
    {
        var factory = _services.GetRequiredService<IPosConnectionFactory>();
        var lease = await factory.AcquireWriteConnectionAsync().ConfigureAwait(false);
        await using (lease.ConfigureAwait(false))
        {
            await using var command = lease.Connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

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
