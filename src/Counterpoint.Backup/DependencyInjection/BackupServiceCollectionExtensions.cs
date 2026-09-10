using System;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Security;
using Counterpoint.Backup.Orchestration;
using Counterpoint.Backup.Restore;
using Counterpoint.Backup.Snapshots;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Counterpoint.Backup.DependencyInjection;

/// <summary>
/// Wires the backup pipeline into the composition root. Only the composition root calls this -
/// Counterpoint.Ui never references this assembly (CLAUDE.md "Project boundaries").
/// </summary>
public static class BackupServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SnapshotService"/>, <see cref="RestoreService"/>, the P1-T15 scheduler
    /// and the manual-backup and guided-restore services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="snapshotOptions">
    /// Where the encrypted backup file is written. The composition root builds this from
    /// <c>PosDataDirectory.SnapshotDirectory</c>, since <c>Counterpoint.Backup</c> may not
    /// reference <c>Counterpoint.Infrastructure</c> to resolve it itself.
    /// </param>
    /// <param name="schedulerOptions">
    /// How often <see cref="BackupScheduler"/> checks the clock. Defaults to
    /// <see cref="BackupSchedulerOptions.Default"/>.
    /// </param>
    /// <remarks>
    /// <b>The owner-only interfaces are decorated here, not in the composition root.</b>
    /// <see cref="IManualBackupTrigger"/> and <see cref="IGuidedRestoreService"/> are implemented
    /// by classes internal to this assembly (<see cref="BackupOrchestrator"/>,
    /// <see cref="GuidedRestoreService"/>), so only this project - not <c>Counterpoint.App</c> -
    /// can name them to build and decorate one, the same way <c>Counterpoint.Application</c>'s own
    /// <c>AddCounterpointSecurity</c> and <c>AddCounterpointCatalogue</c> extensions do for their
    /// owner-only services (SRS NFR-S2, AC-17).
    /// </remarks>
    public static IServiceCollection AddCounterpointBackup(
        this IServiceCollection services,
        SnapshotOptions snapshotOptions,
        BackupSchedulerOptions? schedulerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(snapshotOptions);

        services.AddSingleton(snapshotOptions);
        services.AddSingleton(schedulerOptions ?? BackupSchedulerOptions.Default);

        // Shared with password hashing (SRS FR-1.3) when the composition root has already
        // registered one; a caller that wires only Counterpoint.Backup - a test, say - still gets
        // a sane default rather than a missing-service exception.
        services.TryAddSingleton(Argon2Parameters.Default);

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<SnapshotService>();
        services.AddSingleton<RestoreService>();

        // P1-T15: the daily/shift-close scheduler and the owner's "Backup now" button share one
        // pipeline (BackupOrchestrator). IBackupOrchestrator is undecorated - the schedule and a
        // shift close call it with nobody signed in - while IManualBackupTrigger is the same
        // instance behind the owner-only check (SRS FR-11.1, FR-11.2, NFR-S2, AC-17).
        services.AddSingleton<BackupOrchestrator>();
        services.AddSingleton<IBackupOrchestrator>(p => p.GetRequiredService<BackupOrchestrator>());
        services.AddSingleton<IManualBackupTrigger>(p => RoleAuthorisation.Decorate<IManualBackupTrigger>(
            p.GetRequiredService<BackupOrchestrator>(),
            p.GetRequiredService<ISession>()));

        // Registered as itself too - the same shape as Counterpoint.Devices.Printing.PrintWorker -
        // so a test can build the same container and drive one tick by hand without a host
        // starting a polling loop underneath it.
        services.AddSingleton<BackupScheduler>();
        services.AddSingleton<IHostedService>(p => p.GetRequiredService<BackupScheduler>());

        // P1-T15: the guided restore wizard's backend (SRS FR-11.12, FR-11.13) - owner-only, the
        // same shape as IManualBackupTrigger above.
        services.AddSingleton<GuidedRestoreService>();
        services.AddSingleton<IGuidedRestoreService>(p => RoleAuthorisation.Decorate<IGuidedRestoreService>(
            p.GetRequiredService<GuidedRestoreService>(),
            p.GetRequiredService<ISession>()));

        return services;
    }
}
