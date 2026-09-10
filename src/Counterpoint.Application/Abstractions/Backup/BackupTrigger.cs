namespace Counterpoint.Application.Abstractions.Backup;

/// <summary>What asked for a backup (SRS FR-11.1, FR-11.2, P1-T15).</summary>
public enum BackupTrigger
{
    /// <summary>The daily schedule, at <c>backup.daily_time</c> (FR-11.1).</summary>
    Scheduled,

    /// <summary>The owner pressed "Backup now" (FR-11.2).</summary>
    Manual,

    /// <summary>A shift just closed and <c>backup.on_shift_close</c> is true (FR-11.1).</summary>
    ShiftClose,
}
