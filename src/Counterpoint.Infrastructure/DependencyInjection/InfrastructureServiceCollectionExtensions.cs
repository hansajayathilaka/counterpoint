using System;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Infrastructure.Data;
using Counterpoint.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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

        // Registered, not invoked. Calling ApplyPendingMigrationsAsync at start-up belongs to
        // P0-T06, which builds the composition root - Counterpoint.Ui has no DI container yet.
        // Until then the runner is driven by the integration tests.
        services.AddSingleton<MigrationRunner>();

        return services;
    }
}
