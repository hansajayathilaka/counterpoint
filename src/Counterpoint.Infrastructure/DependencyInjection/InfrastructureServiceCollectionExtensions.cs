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
    /// Registers the data directory, key store, connection factory, unit of work and
    /// <see cref="PosDbContext"/>.
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

        // One keyed, PRAGMA'd connection per scope, owned and disposed by the context. EF is the
        // write and migration path; the single-writer rule is enforced by IUnitOfWork above.
        services.AddDbContext<PosDbContext>((provider, options) =>
            options.UseSqlite(
                provider.GetRequiredService<IPosConnectionFactory>().OpenConfiguredConnection(),
                contextOwnsConnection: true));

        return services;
    }
}
