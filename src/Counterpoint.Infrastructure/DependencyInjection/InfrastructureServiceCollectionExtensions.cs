using System;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Sales;
using Counterpoint.Infrastructure.Audit;
using Counterpoint.Infrastructure.Backup;
using Counterpoint.Infrastructure.Catalogue;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Inventory;
using Counterpoint.Infrastructure.Printing;
using Counterpoint.Infrastructure.Sales;
using Counterpoint.Infrastructure.Security;
using Counterpoint.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Counterpoint.Infrastructure.DependencyInjection;

/// <summary>
/// Wires the local database into the composition root. Only the composition root calls this -
/// Counterpoint.Ui never references this assembly (CLAUDE.md "Project boundaries").
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Registers the data directory, key store, connection factory, unit of work and the
    /// <see cref="PosDbContext"/> factory.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="dataDirectoryOverride">
    /// Optional data directory. Null uses the platform default. Resolution happens here, at
    /// start-up, so an unusable folder is refused before the sales screen ever opens.
    /// </param>
    public static IServiceCollection AddCounterpointInfrastructure(
        this IServiceCollection services,
        string? dataDirectoryOverride = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var dataDirectory = PosDataDirectory.Resolve(dataDirectoryOverride).EnsureCreated();

        services.AddSingleton(dataDirectory);
        services.AddSingleton<IDatabaseKeyStore>(provider =>
            DatabaseKeyStoreFactory.Create(provider.GetRequiredService<PosDataDirectory>()));

        // The backup passphrase never goes into app_setting - that table lives inside the
        // database the backup is a copy of (SRS FR-10.7, NFR-S6). Registered as the concrete
        // type, not IBackupPassphraseStore: replacing the passphrase is owner-only, and the
        // composition root registers only the role-decorated interface, so first run's
        // internal SetInitialPassphrase seam is the one thing here allowed to hold this
        // directly (SRS NFR-S2, AC-17).
        services.AddSingleton(provider =>
            BackupPassphraseStoreFactory.Create(provider.GetRequiredService<PosDataDirectory>()));

        services.AddSingleton<PosConnectionFactory>();
        services.AddSingleton<IPosConnectionFactory>(provider =>
            provider.GetRequiredService<PosConnectionFactory>());

        services.AddSingleton<SqliteUnitOfWork>();
        services.AddSingleton<IUnitOfWork>(provider => provider.GetRequiredService<SqliteUnitOfWork>());

        // No AddDbContext, on purpose. AddDbContext hands every scope its own connection, which
        // would write outside the write gate and outside the open transaction - two writers on
        // one file. PosDbContext is only reachable through the unit of work's lease, and its
        // constructor is internal so nothing outside this assembly can go around that.
        services.AddSingleton<IPosDbContextFactory>(provider =>
            provider.GetRequiredService<SqliteUnitOfWork>());

        services.AddSingleton<MigrationRunner>();
        services.AddSingleton<FirstRunSeeder>();

        // The ports the Application layer states it needs. Every one of them is stateless and
        // reaches the database only through the unit of work above, so a singleton each.
        services.AddSingleton<IDocumentNumberAllocator, SqliteDocumentNumberAllocator>();
        services.AddSingleton<IProductLookup, SqliteProductLookup>();
        services.AddSingleton<ITillSessionProvider, SqliteTillSessionProvider>();
        services.AddSingleton<ISaleWriter, SqliteSaleWriter>();

        // P1-T10: reads a completed sale back for cancellation (SRS FR-3.34).
        services.AddSingleton<ISaleLookup, SqliteSaleLookup>();
        services.AddSingleton<IStockLedger, SqliteStockLedger>();

        // P1-T07: the ledger's projection rebuild, its startup consistency check, and the
        // stock enquiry screen's read side (SRS FR-4, DM-05, SAD §3).
        services.AddSingleton<IRebuildStockBalance, RebuildStockBalanceCommand>();
        services.AddSingleton<IStockConsistencyCheck, SqliteStockConsistencyCheck>();
        services.AddSingleton<IStockPositionReader, SqliteStockPositionReader>();
        services.AddSingleton<IAuditTrail, SqliteAuditTrail>();
        services.AddSingleton<IPrintJobOutbox, SqlitePrintJobOutbox>();
        services.AddSingleton<IUserStore, SqliteUserStore>();
        services.AddSingleton<ISecurityPolicyStore, SqliteSecurityPolicyStore>();
        services.AddSingleton<ISettingStore, SqliteSettingStore>();
        services.AddSingleton<INumberSequenceConfiguration, SqliteNumberSequenceConfiguration>();
        services.AddSingleton<ITaxClassSeed, SqliteTaxClassSeed>();

        // P1-T04: category, brand, uom, tax_class, supplier, customer reference-data maintenance
        // (SRS FR-2.20, FR-2.21, FR-6), and the default unit/category set the first-run wizard
        // seeds from (docs/01_DATA_MODEL.md §11).
        services.AddSingleton<ICategoryStore, SqliteCategoryStore>();
        services.AddSingleton<IBrandStore, SqliteBrandStore>();
        services.AddSingleton<IUomStore, SqliteUomStore>();
        services.AddSingleton<ITaxClassStore, SqliteTaxClassStore>();
        services.AddSingleton<ISupplierStore, SqliteSupplierStore>();
        services.AddSingleton<ICustomerStore, SqliteCustomerStore>();
        services.AddSingleton<ICatalogueReferenceDataSeed, SqliteCatalogueReferenceDataSeed>();

        // P1-T05: product, variant and UOM conversion maintenance (SRS FR-2.1-FR-2.8, FR-3.6, AC-08).
        services.AddSingleton<IProductStore, SqliteProductStore>();

        // P1-T08: pricing and discounts (SRS FR-2.13-FR-2.19). IPriceChangeLogStore and
        // IPriceQuery are read/maintenance ports with no role requirement of their own - the
        // owner-only surfaces built on top of them (IProductMaintenance's price change,
        // IBulkPriceUpdateService) are wired decorated in the composition root, the same as
        // IProductStore above. IPriceTierQuery is read-only until P5-T01 adds the maintenance
        // screen that writes price_tier rows.
        services.AddSingleton<IPriceChangeLogStore, SqlitePriceChangeLogStore>();
        services.AddSingleton<IPriceTierQuery, SqlitePriceTierQuery>();
        services.AddSingleton<IPriceQuery, SqlitePriceQuery>();

        // P1-T06: barcodes and product search (SRS FR-2.9-2.12, FR-2.24, NFR-P1, NFR-P2).
        // IProductSearchService and IReindexSearchCommand are read/maintenance ports with no
        // role requirement of their own - IProductSearchService is on the counter-search path a
        // cashier is standing at, exactly like IProductLookup above, and IReindexSearchCommand
        // carries no data a session-less caller could misuse. IBarcodeStore is registered here as
        // the port ProductMaintenance's sibling BarcodeMaintenanceService is built against; the
        // owner-only IBarcodeMaintenance itself is wired decorated in the composition root, the
        // same as IProductMaintenance.
        services.AddSingleton<IBarcodeStore, SqliteBarcodeStore>();
        services.AddSingleton<IBarcodeSerialAllocator, SqliteBarcodeSerialAllocator>();
        services.AddSingleton<IProductSearchService, SqliteProductSearchService>();
        services.AddSingleton<IReindexSearchCommand, SqliteReindexSearchCommand>();

        // P1-T09: held bills (F5/F6, SRS FR-3.32, FR-3.33). No role requirement - holding and
        // recalling a bill is an ordinary cashier action.
        services.AddSingleton<IHeldBillService, SqliteHeldBillService>();

        // P0-T07: the one seam Counterpoint.Backup reaches a SQLCipher connection through,
        // because it may not reference this assembly (CLAUDE.md "Project boundaries").
        services.AddSingleton<IDatabaseSnapshotSource, SqliteDatabaseSnapshotSource>();
        services.AddSingleton<IBackupRecordStore, SqliteBackupRecordStore>();

        // A clock, not DateTimeOffset.Now: created-at and printed-at stamps have to be
        // controllable from a test, and TimeProvider is the framework's answer.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
