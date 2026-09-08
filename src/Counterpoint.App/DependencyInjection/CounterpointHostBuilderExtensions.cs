using System;
using Avalonia.Threading;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Application.Settings.FirstRun;
using Counterpoint.Devices.DependencyInjection;
using Counterpoint.Domain.Services;
using Counterpoint.Infrastructure.DependencyInjection;
using Counterpoint.Ui.ViewModels;
using Counterpoint.Ui.ViewModels.FirstRun;
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

        builder.Services.AddCounterpointSecurity();

        // The screens.
        builder.Services.AddSingleton<LoginViewModel>();
        builder.Services.AddSingleton<SalesViewModel>();
        builder.Services.AddSingleton<UserAdminViewModel>();
        builder.Services.AddSingleton<FirstRunWizardViewModel>();

        // The settings screen is handed the one thing it cannot get from Counterpoint.Ui: a way
        // back onto the thread the window lives on. ISettings.Changed is raised by whichever
        // thread committed the write, and a viewmodel that reloaded itself from a thread-pool
        // thread would be updating bindings from the wrong thread.
        builder.Services.AddSingleton(provider => new SettingsViewModel(
            provider.GetRequiredService<ISettings>(),
            provider.GetRequiredService<IBackupPassphraseStore>(),
            action => Dispatcher.UIThread.Post(action)));

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
}
