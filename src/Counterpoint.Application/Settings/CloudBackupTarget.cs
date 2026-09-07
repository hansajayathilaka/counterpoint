namespace Counterpoint.Application.Settings;

/// <summary>Where the off-site copy of a backup goes (SRS FR-10.7, FR-11, Q-D).</summary>
public enum CloudBackupTarget
{
    /// <summary>No off-site copy. Local folder and USB only.</summary>
    None = 0,

    /// <summary>
    /// Google Drive - the shop's answer to Q-D. Naming the target here is a setting, not an
    /// implementation: no cloud code exists yet, and none may run on a sale path ever
    /// (CLAUDE.md "Not cloud-dependent"). Phase 4 builds the uploader.
    /// </summary>
    GoogleDrive = 1,
}
