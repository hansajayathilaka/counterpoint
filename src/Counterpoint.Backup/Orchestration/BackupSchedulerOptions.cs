using System;

namespace Counterpoint.Backup.Orchestration;

/// <param name="PollInterval">
/// How often <see cref="BackupScheduler"/> checks the clock against <c>backup.daily_time</c>.
/// Short enough that a backup runs within one interval of becoming due, long enough that it costs
/// nothing worth measuring against the sale path it must never compete with (CLAUDE.md invariant
/// 7). A minute is the shop's own precision - <c>backup.daily_time</c> has no seconds.
/// </param>
public sealed record BackupSchedulerOptions(TimeSpan PollInterval)
{
    public static BackupSchedulerOptions Default { get; } = new(TimeSpan.FromMinutes(1));
}
