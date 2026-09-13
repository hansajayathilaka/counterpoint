using System;
using System.IO;
using Avalonia.Threading;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Devices;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Catalogue;
using Counterpoint.Application.Dashboard;
using Counterpoint.Application.Exchanges;
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
using Counterpoint.Domain.Services;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.DependencyInjection;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.Catalogue;
using Counterpoint.Ui.ViewModels.FirstRun;
using Counterpoint.Ui.ViewModels.Labels;
using Counterpoint.Ui.ViewModels.Purchasing;
using Counterpoint.Ui.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Counterpoint.App.DependencyInjection;

/// <summary>
/// The whole of the wiring: the adapters, the use cases and the screens.
/// </summary>
/// <remarks>
/// This is the only place in the solution that can see both <c>Counterpoint.Ui</c> and
/// <c>Counterpoint.Infrastructure</c>. Everything above meets everything below exactly here,
/// through interfaces, which is what makes "authorisation is checked in the Application layer,
/// not the UI" a structural fact rather than a promise (SRS NFR-S2, AC-17).
/// </remarks>
internal static class CounterpointHostBuilderExtensions
{
    /// <summary>Registers every service the application needs.</summary>
    internal static HostApplicationBuilder ConfigureCounterpoint(this HostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Adapters. Resolving the data directory happens here, at start-up, so an unusable
        // folder is refused before the sales screen ever opens (engineering guide §4.9).
        builder.Services.AddCounterpointInfrastructure();
        builder.Services.AddCounterpointDevices();

        builder.Services.AddCounterpointSettings();

        // Use cases. Kept in step with the same lines in
        // tests/Counterpoint.Integration.Tests/Sales/SaleFixture.cs, which composes the same
        // container without Avalonia in it.
        builder.Services.AddSingleton<IScanItem, ScanItemHandler>();
        builder.Services.AddSingleton<CompleteSaleHandler>();
        builder.Services.AddSingleton<ICompleteSale>(p => p.GetRequiredService<CompleteSaleHandler>());
        builder.Services.AddSingleton<IQuoteSale>(p => p.GetRequiredService<CompleteSaleHandler>());

        // P1-T10: bill cancellation (SRS FR-3.34) - owner-only, wired exactly as the catalogue
        // maintenance interfaces below are (NFR-S2, AC-17).
        builder.Services.AddSingleton<ICancelSale>(p => RoleAuthorisation.Decorate<ICancelSale>(
            ActivatorUtilities.CreateInstance<CancelSaleHandler>(p),
            p.GetRequiredService<ISession>()));

        // P1-T11: reprint (SRS FR-3.36, FR-7.5, FR-7.6). Any signed-in cashier may reprint
        // (SRS §3.3 ROLE-1), so this is undecorated, the same shape as ICompleteSale above.
        builder.Services.AddSingleton<IReprintReceipt>(
            p => ActivatorUtilities.CreateInstance<ReprintReceiptHandler>(p));

        // P1-T07: the stock enquiry screen (F11). No [RequiresRole] - "check stock" is a
        // cashier capability (Counterpoint.Domain.Security.Role) - so the whole result comes
        // back and StockEnquiryService itself strips cost for anyone who is not signed in as
        // owner (CLAUDE.md invariant 8).
        builder.Services.AddSingleton<IStockEnquiry, StockEnquiryService>();

        // P1-T14: opening a shift (SRS FR-8.1) - a cashier capability like ICompleteSale above,
        // not owner-only, so it is registered plain. Built through ActivatorUtilities because its
        // constructor takes the concrete Session, not ISession, exactly the seam
        // AuthenticationService uses to record who is signed in (see Session's own remarks).
        builder.Services.AddSingleton<IOpenShift>(p =>
            ActivatorUtilities.CreateInstance<OpenShiftHandler>(p));

        // P1-T14: the home-screen dashboard (SRS FR-9.7). No [RequiresRole] - none of its six
        // figures is cost, margin or profit (FR-9.4 reserves those for the owner role), the same
        // reasoning as IStockEnquiry above.
        builder.Services.AddSingleton<IDashboardQueries, DashboardService>();

        builder.Services.AddCounterpointSecurity();
        builder.Services.AddCounterpointCatalogue();

        // P2-T06: suppliers and purchase orders (SRS FR-4.5, FR-4.6, FR-4.10) - owner-only,
        // wired exactly as the catalogue-maintenance interfaces above (NFR-S2, AC-17).
        builder.Services.AddSingleton<IPurchaseOrderService>(p => RoleAuthorisation.Decorate<IPurchaseOrderService>(
            ActivatorUtilities.CreateInstance<PurchaseOrderService>(p),
            p.GetRequiredService<ISession>()));

        // P2-T07: goods receipt (SRS FR-4.7, FR-4.8, AC-08) - owner-only, wired exactly as
        // IPurchaseOrderService above. Depends on IPurchaseOrderService itself (to recompute a
        // linked order's status) and ILabelPrintService (registered further below); the factory
        // lambda resolves both lazily, once every registration in this method has run, so the
        // order the two lines appear in does not matter.
        builder.Services.AddSingleton<IGoodsReceiptService>(p => RoleAuthorisation.Decorate<IGoodsReceiptService>(
            ActivatorUtilities.CreateInstance<GoodsReceiptService>(p),
            p.GetRequiredService<ISession>()));

        // P1-T08: pricing and discounts (SRS FR-2.13-FR-2.19, FR-3.7-FR-3.10, Q-12).
        // IDiscountAuthorisationService is not owner-only - a cashier applies a discount inside
        // the cap without needing anyone's role, and going over it is gated by an OverrideToken,
        // not RequiresRoleAttribute - so it is registered plain, undecorated, the same as
        // IRoundingPolicy above.
        builder.Services.AddSingleton<IDiscountAuthorisationService, DiscountAuthorisationService>();

        // P2-T01: the return policy engine (SRS FR-5, BR-*, FR-10.5, Q-03). Not owner-only either
        // - a cashier processes an in-window, receipted return without needing anyone's role, and
        // every exception to that is gated by an OverrideToken, not RequiresRoleAttribute, the
        // same reasoning as IDiscountAuthorisationService immediately above.
        builder.Services.AddSingleton<IReturnPolicyAuthorisationService, ReturnPolicyAuthorisationService>();

        // P2-T02: linked returns (SRS FR-5.1-FR-5.10, AC-03, AC-06). Not owner-only either, the
        // same reasoning as IReturnPolicyAuthorisationService immediately above - a cashier takes
        // an ordinary, in-window, receipted return without needing anyone's role.
        builder.Services.AddSingleton<ICreateReturn, CreateReturnHandler>();

        // P2-T03: unlinked returns (SRS FR-5.19, NFR-S2). Not owner-only in its own right either -
        // the cashier still takes the return and stays signed in throughout (SRS FR-1.7); what
        // makes this path high-friction is that CreateUnlinkedReturnCommand.Override is mandatory
        // on every call, not that the method needs anyone's role.
        builder.Services.AddSingleton<ICreateUnlinkedReturn, CreateUnlinkedReturnHandler>();

        // P2-T04: exchanges (SRS FR-5 exchange, AC-04). Not owner-only either, the same reasoning
        // as ICreateReturn above - a cashier takes an ordinary, in-window, receipted exchange
        // without needing anyone's role; every exception to that is gated by the same override
        // tokens a standalone return already uses.
        builder.Services.AddSingleton<ICreateExchange, CreateExchangeHandler>();

        // Resolved a second time, deliberately: AddCounterpointInfrastructure resolves and
        // registers its own PosDataDirectory internally and does not hand it back, and changing
        // that already-tested signature is out of scope here. Resolve() and EnsureCreated() are
        // both idempotent - same validation, same "create if missing" - so this costs a
        // redundant filesystem check at start-up, not a second source of truth. Registered after
        // AddCounterpointSecurity so its TryAddSingleton(Argon2Parameters.Default) genuinely finds
        // one already there rather than racing it. See P0-T07.
        var dataDirectory = PosDataDirectory.Resolve().EnsureCreated();
        builder.Services.AddCounterpointBackup(new SnapshotOptions(dataDirectory.SnapshotDirectory));

        // The screens.
        builder.Services.AddSingleton<LoginViewModel>();
        builder.Services.AddSingleton<SalesViewModel>();
        builder.Services.AddSingleton<UserAdminViewModel>();
        builder.Services.AddSingleton<FirstRunWizardViewModel>();

        // P1-T04: the catalogue reference-data screen, one tab viewmodel per entity, composed
        // into one CatalogueViewModel (SRS FR-2.20, FR-2.21, FR-6).
        builder.Services.AddSingleton<CategoryTabViewModel>();
        builder.Services.AddSingleton<BrandTabViewModel>();
        builder.Services.AddSingleton<UomTabViewModel>();
        builder.Services.AddSingleton<TaxClassTabViewModel>();
        builder.Services.AddSingleton<SupplierTabViewModel>();
        builder.Services.AddSingleton<CustomerTabViewModel>();

        // P1-T05: the product editor tab - variant grid, UOM grid, variant matrix generator
        // (SRS FR-2.1-FR-2.8, FR-3.6, AC-08).
        builder.Services.AddSingleton<ProductTabViewModel>();

        // P1-T13: the import/export tab (SRS FR-2.22, FR-2.23, AC-07, Q-08). Takes the same
        // role-decorated ICatalogueImportService AddCounterpointCatalogue registers below, plus
        // ISpreadsheetReader (registered in AddCounterpointInfrastructure) to read a file's
        // headers for the mapping grid - reading headers is not a use case with its own
        // Application-layer authorisation, the same as IStockEnquiry above.
        builder.Services.AddSingleton<ImportTabViewModel>();

        builder.Services.AddSingleton<CatalogueViewModel>();

        // P2-T06: the purchase-order screen (SRS FR-4.5, FR-4.6, FR-4.10).
        builder.Services.AddSingleton<PurchaseOrderViewModel>();

        // P1-T12: label printing (SRS FR-2.10, FR-2.12). ILabelPrintService is owner-only, wired
        // exactly as the catalogue-maintenance interfaces are in AddCounterpointCatalogue; it
        // lives here instead because it depends on ILabelPrinter/ILabelRenderer, which only exist
        // once AddCounterpointDevices has run above.
        builder.Services.AddSingleton<ILabelPrintService>(p => RoleAuthorisation.Decorate<ILabelPrintService>(
            ActivatorUtilities.CreateInstance<LabelPrintService>(p),
            p.GetRequiredService<ISession>()));
        builder.Services.AddSingleton<LabelPrintViewModel>();

        // P1-T11: the print queue screen (pending and failed print_job rows, with retry).
        builder.Services.AddSingleton<PrintQueueViewModel>();

        // The settings screen is handed the one thing it cannot get from Counterpoint.Ui: a way
        // back onto the thread the window lives on. ISettings.Changed is raised by whichever
        // thread committed the write, and a viewmodel that reloaded itself from a thread-pool
        // thread would be updating bindings from the wrong thread.
        builder.Services.AddSingleton(provider => new SettingsViewModel(
            provider.GetRequiredService<ISettings>(),
            provider.GetRequiredService<IBackupPassphraseStore>(),
            action => Dispatcher.UIThread.Post(action),
            provider.GetRequiredService<IReceiptTemplatePreviewService>(),
            provider.GetRequiredService<IManualBackupTrigger>()));

        // P1-T15: the guided restore wizard (SRS FR-11.12).
        builder.Services.AddSingleton<RestoreWizardViewModel>();

        return builder;
    }

    /// <summary>
    /// The settings framework, the first-run wizard's headless half, and the rounding policy the
    /// shop's own settings drive (SRS FR-10, NFR-M1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is where the rounding rule stopped being a constant.</b> It used to be
    /// <c>new HalfAwayFromZeroRounding(decimalPlaces: 2)</c> right here, with a note saying it
    /// became a setting in P1-T03. It has: <see cref="SettingsRoundingPolicy"/> reads the rule and
    /// the decimal places from <see cref="ISettings"/> on every use, so changing them changes the
    /// next line total and the next printed amount without a restart (FR-10.2).
    /// </para>
    /// <para>
    /// Singletons, and the cache behind them is one immutable reference, so every reader in the
    /// process sees the same settings and sees a change the moment it commits. <c>ISettings</c>
    /// must be loaded before anything reads a setting; <c>Program.PrepareDatabaseAsync</c> does
    /// that, after the migrations and before the window opens.
    /// </para>
    /// <para>
    /// <b><see cref="ISettings"/> is registered decorated</b>, the same way
    /// <see cref="IUserAdministration"/> is, because <c>SaveAsync</c> and <c>UpdateAsync</c> carry
    /// <see cref="RequiresRoleAttribute"/>: settings are the owner's (SRS §3.3 ROLE-2, FR-1.2,
    /// FR-1.6, NFR-S2, AC-17). The read side carries no attribute, so the proxy forwards
    /// <c>LoadAsync</c> and every group property untouched - which is what lets
    /// <c>Program.PrepareDatabaseAsync</c> load the settings before anyone has signed in, and lets
    /// <see cref="SettingsRoundingPolicy"/> read the decimal places on a cashier's every line.
    /// </para>
    /// <para>
    /// The concrete <see cref="SettingsService"/> keeps a registration of its own, unlike
    /// <c>UserAdministrationService</c>, because <c>FirstRunSetupService</c> genuinely needs it:
    /// first run has nobody signed in, so it writes through the internal <c>SaveAsAsync</c> that
    /// is told which owner is acting. Both classes are internal, so only this composition root and
    /// the two test ones can name them at all.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddCounterpointSettings(this IServiceCollection services)
    {
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettings>(p => RoleAuthorisation.Decorate<ISettings>(
            p.GetRequiredService<SettingsService>(),
            p.GetRequiredService<ISession>()));
        services.AddSingleton<IRoundingPolicy, SettingsRoundingPolicy>();

        services.AddSingleton<IFirstRunSetup, FirstRunSetupService>();

        // Same shape as ISettings: replacing the backup passphrase is owner-only, so only the
        // role-decorated interface is handed out. FirstRunSetupService takes the concrete
        // BackupPassphraseStore (registered in AddCounterpointInfrastructure) so it can reach
        // the internal SetInitialPassphrase seam with nobody signed in (SRS NFR-S2, AC-17).
        services.AddSingleton<IBackupPassphraseStore>(p => RoleAuthorisation.Decorate<IBackupPassphraseStore>(
            p.GetRequiredService<BackupPassphraseStore>(),
            p.GetRequiredService<ISession>()));

        return services;
    }

    /// <summary>
    /// Authentication, the session, and the role check in front of every owner-only service
    /// (SRS FR-1, NFR-S1, NFR-S2, NFR-S9, AC-17).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Singletons throughout: one machine, one till, one active session (C-01). The session is
    /// registered twice on purpose - as <see cref="Session"/> for
    /// <see cref="AuthenticationService"/>, which is the only thing allowed to change it, and as
    /// <see cref="ISession"/> for everything that may only read it.
    /// </para>
    /// <para>
    /// <b>The registration that matters is <see cref="IUserAdministration"/>.</b> The concrete
    /// service is never registered at all: it is built inside the factory, decorated, and only
    /// the decorated interface goes into the container. Nothing can ask the provider for the
    /// undecorated object because there is no registration to resolve, and nothing outside
    /// <c>Counterpoint.Application</c> can construct one either, because the class is
    /// <c>internal</c> and this project sees it only through an <c>InternalsVisibleTo</c> seam
    /// granted for exactly this. That is what makes the role check unavoidable rather than
    /// customary.
    /// </para>
    /// <para>
    /// Every future service carrying <see cref="RequiresRoleAttribute"/> is registered this same
    /// way, and
    /// <c>ArchitectureTests.ConcreteOwnerOnlyApplicationServicesAreNotPublic</c> fails the build
    /// if one is written public - which is the only way the old, unguarded registration could
    /// come back.
    /// </para>
    /// </remarks>
    private static IServiceCollection AddCounterpointSecurity(this IServiceCollection services)
    {
        services.AddSingleton(Argon2Parameters.Default);
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        services.AddSingleton<Session>();
        services.AddSingleton<ISession>(p => p.GetRequiredService<Session>());

        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IInitialOwnerSetup, InitialOwnerSetupService>();
        services.AddSingleton<IOwnerOverrideService, OwnerOverrideService>();
        services.AddSingleton<SecurityPolicyRecorder>();

        services.AddSingleton<IUserAdministration>(p => RoleAuthorisation.Decorate<IUserAdministration>(
            ActivatorUtilities.CreateInstance<UserAdministrationService>(p),
            p.GetRequiredService<ISession>()));

        return services;
    }

    /// <summary>
    /// Category, brand, unit, tax class, supplier and customer maintenance (SRS FR-2.20, FR-2.21,
    /// FR-6). Every one of the six is owner-only, registered exactly as
    /// <see cref="IUserAdministration"/> is: the concrete service is built inside the factory,
    /// decorated, and only the decorated interface goes into the container - so nothing can reach
    /// one of these without going through <see cref="RoleAuthorisation"/> first (SRS NFR-S2,
    /// AC-17).
    /// </summary>
    private static IServiceCollection AddCounterpointCatalogue(this IServiceCollection services)
    {
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

        // P1-T06: barcode administration and the internal-barcode generator - wired exactly as
        // the seven above (SRS FR-2.9, FR-2.10, FR-2.24, NFR-S2, AC-17). IProductSearchService and
        // IReindexSearchCommand carry no RequiresRoleAttribute, so they are registered undecorated
        // by AddCounterpointInfrastructure and never appear here.
        services.AddSingleton<IBarcodeMaintenance>(p => RoleAuthorisation.Decorate<IBarcodeMaintenance>(
            ActivatorUtilities.CreateInstance<BarcodeMaintenanceService>(p),
            p.GetRequiredService<ISession>()));

        // P1-T08: bulk price update by category, brand or supplier (SRS FR-2.19) - wired exactly
        // as the eight above.
        services.AddSingleton<IBulkPriceUpdateService>(p => RoleAuthorisation.Decorate<IBulkPriceUpdateService>(
            ActivatorUtilities.CreateInstance<BulkPriceUpdateService>(p),
            p.GetRequiredService<ISession>()));

        // P1-T13: spreadsheet catalogue import and export (SRS FR-2.22, FR-2.23) - wired exactly
        // as the nine above.
        services.AddSingleton<ICatalogueImportService>(p => RoleAuthorisation.Decorate<ICatalogueImportService>(
            ActivatorUtilities.CreateInstance<CatalogueImportService>(p),
            p.GetRequiredService<ISession>()));

        return services;
    }
}
