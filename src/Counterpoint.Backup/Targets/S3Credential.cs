using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// Everything <see cref="S3CompatibleTarget"/> needs to reach one bucket, held as one opaque
/// string in <c>IBackupTargetCredentialStore</c> under
/// <see cref="BackupTargetCredentialKeys.S3Compatible"/> - never split across the credential store
/// and <c>app_setting</c>, so <c>app_setting</c> never carries so much as an access key id
/// (SRS FR-11.5, NFR-S6, P4-T01: "config holds a reference key only").
/// </summary>
/// <param name="Endpoint">
/// The provider's base URL, for example <c>https://storage.googleapis.com</c> or
/// <c>https://&lt;account&gt;.r2.cloudflarestorage.com</c>.
/// </param>
/// <param name="Region">
/// The SigV4 region token. Some providers ignore it or expect a fixed value such as <c>auto</c>;
/// an empty value defaults to <c>us-east-1</c>, the same fallback the AWS SDK itself uses.
/// </param>
/// <param name="Bucket">The bucket name.</param>
/// <param name="AccessKeyId">The access key id.</param>
/// <param name="SecretAccessKey">The secret access key. This is the actual secret.</param>
public sealed record S3Credential(
    string Endpoint,
    string Region,
    string Bucket,
    string AccessKeyId,
    string SecretAccessKey)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    internal string RegionOrDefault => string.IsNullOrWhiteSpace(Region) ? "us-east-1" : Region;

    /// <summary>Serialises to the JSON text stored in the credential store.</summary>
    internal string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Parses the JSON text held in the credential store.
    /// </summary>
    /// <exception cref="FormatException">The text is not a valid S3 credential.</exception>
    internal static S3Credential Parse(string json)
    {
        try
        {
            var credential = JsonSerializer.Deserialize<S3Credential>(json, JsonOptions);
            if (credential is null
                || string.IsNullOrWhiteSpace(credential.Endpoint)
                || string.IsNullOrWhiteSpace(credential.Bucket)
                || string.IsNullOrWhiteSpace(credential.AccessKeyId)
                || string.IsNullOrWhiteSpace(credential.SecretAccessKey))
            {
                throw new FormatException(
                    "The S3-compatible credential must include endpoint, bucket, accessKeyId and secretAccessKey.");
            }

            return credential;
        }
        catch (JsonException ex)
        {
            throw new FormatException("The S3-compatible credential is not valid JSON.", ex);
        }
    }
}
