using System;
using Counterpoint.Application.Security;
using Counterpoint.Backup.Restore;
using Counterpoint.Backup.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Counterpoint.Backup.DependencyInjection;

/// <summary>
/// Wires the backup pipeline into the composition root. Only the composition root calls this -
/// Counterpoint.Ui never references this assembly (CLAUDE.md "Project boundaries").
/// </summary>
public static class BackupServiceCollectionExtensions
{
    /// <summary>Registers <see cref="SnapshotService"/> and <see cref="RestoreService"/>.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="snapshotOptions">
    /// Where the encrypted backup file is written. The composition root builds this from
    /// <c>PosDataDirectory.SnapshotDirectory</c>, since <c>Counterpoint.Backup</c> may not
    /// reference <c>Counterpoint.Infrastructure</c> to resolve it itself.
    /// </param>
    public static IServiceCollection AddCounterpointBackup(
        this IServiceCollection services,
        SnapshotOptions snapshotOptions)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(snapshotOptions);

        services.AddSingleton(snapshotOptions);

        // Shared with password hashing (SRS FR-1.3) when the composition root has already
        // registered one; a caller that wires only Counterpoint.Backup - a test, say - still gets
        // a sane default rather than a missing-service exception.
        services.TryAddSingleton(Argon2Parameters.Default);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<SnapshotService>();
        services.AddSingleton<RestoreService>();

        return services;
    }
}
