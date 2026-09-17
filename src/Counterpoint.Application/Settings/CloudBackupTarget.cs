namespace Counterpoint.Application.Settings;

/// <summary>Where the off-site copy of a backup goes (SRS FR-10.7, FR-11, Q-D).</summary>
/// <remarks>
/// The cloud store is passive and interchangeable behind
/// <c>Counterpoint.Backup.Targets.IBackupTarget</c> (P4-T01, SAD ADR-003) - choosing a different
/// member here and pointing the credential store at a new target's credential is the whole cost
/// of changing the shop's mind, with no code path in a sale, return, price lookup or report ever
/// touching the network either way (CLAUDE.md "Not cloud-dependent").
/// </remarks>
public enum CloudBackupTarget
{
    /// <summary>No off-site copy. Local folder and USB only.</summary>
    None = 0,

    /// <summary>
    /// Google Drive - the shop's answer to Q-D. OAuth device-code flow at setup; only the refresh
    /// token is kept, and only in the protected credential store, never in <c>app_setting</c>
    /// (NFR-S6).
    /// </summary>
    GoogleDrive = 1,

    /// <summary>
    /// Any S3-compatible object store - GCS, Cloudflare R2, Backblaze B2, or an operator-run
    /// bucket (P4-T01). Endpoint, bucket, region and keys live in the credential store, never in
    /// <c>app_setting</c> (NFR-S6).
    /// </summary>
    S3Compatible = 2,

    /// <summary>
    /// A local folder - typically a mapped NAS share - used instead of a true cloud target when
    /// the owner prefers it, and used for testing on a machine with no cloud credentials at all
    /// (P4-T01).
    /// </summary>
    LocalFolder = 3,
}
