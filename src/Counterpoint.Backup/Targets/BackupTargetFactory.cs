using System;
using System.Net.Http;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Settings;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// Builds the <see cref="IBackupTarget"/> for whichever off-site target the shop has chosen,
/// reading its credential from <see cref="IBackupTargetCredentialStore"/> - the one seam that
/// makes switching targets in settings take effect without a restart: nothing here is built once
/// and cached, so the very next call sees whatever is configured now (SRS FR-11.5, P4-T01).
/// </summary>
internal sealed class BackupTargetFactory
{
    private readonly HttpClient _httpClient;
    private readonly IBackupTargetCredentialStore _credentials;
    private readonly TimeProvider _timeProvider;

    // Public constructor on an internal class, deliberately: AddSingleton&lt;BackupTargetFactory&gt;
    // lets the container construct this by reflection, which requires a public constructor even
    // though the type itself stays internal to this assembly (see BackupTargetConnectionTester's
    // own constructor for the same reasoning).
    public BackupTargetFactory(
        HttpClient httpClient, IBackupTargetCredentialStore credentials, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credentials);

        _httpClient = httpClient;
        _credentials = credentials;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Builds the target, using <paramref name="credentialOverride"/> when given, or whatever is
    /// currently stored for it otherwise.
    /// </summary>
    /// <exception cref="BackupTargetException">
    /// <paramref name="target"/> is <see cref="CloudBackupTarget.None"/>, or no usable credential
    /// is available for it (<see cref="BackupTargetFailureKind.ConfigurationError"/>).
    /// </exception>
    internal IBackupTarget Create(CloudBackupTarget target, string? credentialOverride = null)
    {
        if (target == CloudBackupTarget.None)
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.ConfigurationError, "No off-site backup target is configured.");
        }

        var credential = string.IsNullOrWhiteSpace(credentialOverride)
            ? _credentials.TryGetCredential(BackupTargetCredentialKey.For(target))
            : credentialOverride;

        if (string.IsNullOrWhiteSpace(credential))
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.ConfigurationError,
                "No credential is stored for this off-site target yet.");
        }

        try
        {
            return target switch
            {
                CloudBackupTarget.LocalFolder => new LocalFolderTarget(credential),
                CloudBackupTarget.S3Compatible => new S3CompatibleTarget(_httpClient, S3Credential.Parse(credential), _timeProvider),
                CloudBackupTarget.GoogleDrive => new GoogleDriveTarget(_httpClient, GoogleDriveCredential.Parse(credential), _timeProvider),
                _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown off-site backup target."),
            };
        }
        catch (FormatException ex)
        {
            throw new BackupTargetException(BackupTargetFailureKind.ConfigurationError, ex.Message, ex);
        }
    }
}
