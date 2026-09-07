using System;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Devices.DependencyInjection;
using Counterpoint.Domain.Services;
using Counterpoint.Infrastructure.DependencyInjection;
using Counterpoint.Ui.ViewModels;
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

        // The shop's rounding rule. Two decimal places for LKR (Q-01); it becomes a setting in
        // P1-T03, read from app_setting rather than fixed here.
        builder.Services.AddSingleton<IRoundingPolicy>(new HalfAwayFromZeroRounding(decimalPlaces: 2));

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

        return builder;
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
