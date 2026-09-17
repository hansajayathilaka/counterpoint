using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// Everything <see cref="GoogleDriveTarget"/> needs to act as the shop's connected Drive account,
/// held as one opaque string in <c>IBackupTargetCredentialStore</c> under
/// <see cref="BackupTargetCredentialKeys.GoogleDrive"/> (SRS FR-11.5, NFR-S6, P4-T01: "OAuth
/// device-code flow at setup, refresh token only").
/// </summary>
/// <param name="RefreshToken">
/// The long-lived token obtained once, at setup, through Google's OAuth device-code flow. Never
/// an access token - those expire in about an hour and are re-minted from this on every use.
/// </param>
/// <param name="ClientId">The OAuth client id Counterpoint's Drive integration was registered under.</param>
/// <param name="ClientSecret">The OAuth client secret paired with <paramref name="ClientId"/>.</param>
/// <param name="FolderId">
/// The Drive folder backups are filed into. Empty files them at "My Drive"'s root.
/// </param>
public sealed record GoogleDriveCredential(
    string RefreshToken,
    string ClientId,
    string ClientSecret,
    string FolderId = "")
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Serialises to the JSON text stored in the credential store.</summary>
    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Parses the JSON text held in the credential store.
    /// </summary>
    /// <exception cref="FormatException">The text is not a valid Google Drive credential.</exception>
    internal static GoogleDriveCredential Parse(string json)
    {
        try
        {
            var credential = JsonSerializer.Deserialize<GoogleDriveCredential>(json, JsonOptions);
            if (credential is null
                || string.IsNullOrWhiteSpace(credential.RefreshToken)
                || string.IsNullOrWhiteSpace(credential.ClientId)
                || string.IsNullOrWhiteSpace(credential.ClientSecret))
            {
                throw new FormatException(
                    "The Google Drive credential must include refreshToken, clientId and clientSecret.");
            }

            return credential;
        }
        catch (JsonException ex)
        {
            throw new FormatException("The Google Drive credential is not valid JSON.", ex);
        }
    }
}
