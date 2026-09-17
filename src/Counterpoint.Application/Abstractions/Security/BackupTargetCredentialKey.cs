using System;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Abstractions.Security;

/// <summary>
/// The <c>targetKey</c> each <see cref="CloudBackupTarget"/> stores its credential under in
/// <see cref="IBackupTargetCredentialStore"/> (SRS FR-11.5, P4-T01).
/// </summary>
/// <remarks>
/// Lives in the Application layer, not <c>Counterpoint.Backup.Targets</c>, because both
/// <c>Counterpoint.Backup</c> (building a target from its stored credential) and
/// <c>Counterpoint.Ui</c> (saving what the owner just typed, and asking whether a credential is
/// already there) need the same mapping, and <c>Counterpoint.Ui</c> may not reference
/// <c>Counterpoint.Backup</c> (CLAUDE.md "Project boundaries"). One credential per target kind,
/// not per configured value - a shop has exactly one shop, so switching a target's setting and
/// switching back finds the same credential still there.
/// </remarks>
public static class BackupTargetCredentialKey
{
    /// <summary>Google Drive's OAuth client id/secret and refresh token, as JSON.</summary>
    public const string GoogleDrive = "backup-target.google-drive";

    /// <summary>An S3-compatible store's endpoint, region, bucket and access/secret key, as JSON.</summary>
    public const string S3Compatible = "backup-target.s3-compatible";

    /// <summary>
    /// A local/NAS folder's path. Not really a secret, but stored the same way as every other
    /// target's credential, so the settings screen needs only one box regardless of which target
    /// is chosen.
    /// </summary>
    public const string LocalFolder = "backup-target.local-folder";

    /// <summary>The key <paramref name="target"/> stores its credential under.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="CloudBackupTarget.None"/> has no credential.</exception>
    public static string For(CloudBackupTarget target) => target switch
    {
        CloudBackupTarget.GoogleDrive => GoogleDrive,
        CloudBackupTarget.S3Compatible => S3Compatible,
        CloudBackupTarget.LocalFolder => LocalFolder,
        _ => throw new ArgumentOutOfRangeException(
            nameof(target), target, "CloudBackupTarget.None has no credential."),
    };
}
