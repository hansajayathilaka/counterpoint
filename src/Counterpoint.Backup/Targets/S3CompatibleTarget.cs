using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Counterpoint.Application.Abstractions.Backup;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// Any S3-compatible object store - Google Cloud Storage, Cloudflare R2, Backblaze B2, or an
/// operator-run bucket - reached through path-style requests signed with AWS Signature Version 4
/// (SRS FR-11.5, P4-T01).
/// </summary>
/// <remarks>
/// <para>
/// Built on a plain <see cref="HttpClient"/>, not the AWS SDK, to keep the dependency footprint
/// minimal and the whole request/response path fake-able in a test with nothing more than a
/// substituted <see cref="HttpMessageHandler"/> (P4-T01's own guidance).
/// </para>
/// <para>
/// <b>TLS is never disabled.</b> The <see cref="HttpClient"/> this class is handed is the
/// composition root's ordinary one, with the framework's default certificate validation - nothing
/// here touches <c>ServerCertificateCustomValidationCallback</c> (CLAUDE.md, this task's item 5).
/// </para>
/// </remarks>
internal sealed class S3CompatibleTarget : IBackupTarget
{
    private readonly HttpClient _httpClient;
    private readonly S3Credential _credential;
    private readonly TimeProvider _timeProvider;

    internal S3CompatibleTarget(HttpClient httpClient, S3Credential credential, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credential);

        _httpClient = httpClient;
        _credential = credential;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task UploadAsync(
        Stream content,
        string key,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(metadata);

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var payload = buffer.ToArray();

        using var request = new HttpRequestMessage(HttpMethod.Put, ObjectUri(key));
        request.Content = new ByteArrayContent(payload);

        foreach (var (name, value) in metadata)
        {
            request.Content.Headers.TryAddWithoutValidation("x-amz-meta-" + name, value);
        }

        using var response = await SendSignedAsync(request, payload, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "upload the backup", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupObjectInfo>> ListAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var results = new List<BackupObjectInfo>();
        string? continuationToken = null;

        do
        {
            var query = "list-type=2&prefix=" + Uri.EscapeDataString(prefix)
                + (continuationToken is null ? string.Empty : "&continuation-token=" + Uri.EscapeDataString(continuationToken));

            using var request = new HttpRequestMessage(HttpMethod.Get, BucketUri() + "?" + query);
            using var response = await SendSignedAsync(request, [], cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "list the off-site backups", cancellationToken).ConfigureAwait(false);

            var xml = XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var ns = xml.Root?.Name.Namespace ?? XNamespace.None;

            foreach (var contents in xml.Descendants(ns + "Contents"))
            {
                var objectKey = contents.Element(ns + "Key")?.Value ?? string.Empty;
                var size = long.TryParse(contents.Element(ns + "Size")?.Value, out var parsedSize) ? parsedSize : 0L;
                var lastModified = DateTimeOffset.TryParse(
                    contents.Element(ns + "LastModified")?.Value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var parsedDate)
                    ? parsedDate
                    : DateTimeOffset.MinValue;

                results.Add(new BackupObjectInfo(objectKey, size, lastModified));
            }

            var truncated = string.Equals(
                xml.Root?.Element(ns + "IsTruncated")?.Value, "true", StringComparison.OrdinalIgnoreCase);
            continuationToken = truncated ? xml.Root?.Element(ns + "NextContinuationToken")?.Value : null;
        }
        while (continuationToken is not null);

        return results;
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var request = new HttpRequestMessage(HttpMethod.Get, ObjectUri(key));
        using var response = await SendSignedAsync(request, [], cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "download the backup", cancellationToken).ConfigureAwait(false);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var request = new HttpRequestMessage(HttpMethod.Delete, ObjectUri(key));
        using var response = await SendSignedAsync(request, [], cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "remove the backup", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BackupTargetConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BucketUri() + "?list-type=2&max-keys=1");
            using var response = await SendSignedAsync(request, [], cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "connect", cancellationToken).ConfigureAwait(false);

            return BackupTargetConnectionResult.Ok(
                "Connected to bucket '" + _credential.Bucket + "' at " + _credential.Endpoint + ".");
        }
        catch (BackupTargetException ex)
        {
            return BackupTargetConnectionResult.Failed(ex.Message, ex.Kind);
        }
    }

    private Uri BucketUri() => new(TrimEndpoint(_credential.Endpoint) + "/" + _credential.Bucket + "/");

    private Uri ObjectUri(string key) =>
        new(TrimEndpoint(_credential.Endpoint) + "/" + _credential.Bucket + "/" + EncodeKey(key));

    private static string TrimEndpoint(string endpoint) => endpoint.TrimEnd('/');

    private static string EncodeKey(string key) =>
        string.Join('/', key.Split('/').Select(Uri.EscapeDataString));

    private async Task<HttpResponseMessage> SendSignedAsync(
        HttpRequestMessage request, byte[] payload, CancellationToken cancellationToken)
    {
        AwsSigV4Signer.Sign(
            request,
            _credential.RegionOrDefault,
            _credential.AccessKeyId,
            _credential.SecretAccessKey,
            payload,
            _timeProvider.GetUtcNow());

        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.NetworkUnavailable,
                "Could not reach " + _credential.Endpoint + ": " + ex.Message,
                ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.NetworkUnavailable,
                "The connection to " + _credential.Endpoint + " timed out.",
                ex);
        }
    }

    /// <summary>
    /// Classifies and throws for a non-success response, naming the problem from the S3 error
    /// body when one is present (SRS UI-06: "a wrong credential produces a clear message naming
    /// the problem").
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var (code, message) = ParseS3Error(body);

        var kind = response.StatusCode switch
        {
            HttpStatusCode.Forbidden => BackupTargetFailureKind.CredentialRejected,
            HttpStatusCode.Unauthorized => BackupTargetFailureKind.CredentialRejected,
            _ when code is "InvalidAccessKeyId" or "SignatureDoesNotMatch" or "AccessDenied" =>
                BackupTargetFailureKind.CredentialRejected,
            _ when code is "NoSuchBucket" => BackupTargetFailureKind.ConfigurationError,
            HttpStatusCode.NotFound => BackupTargetFailureKind.Other,
            _ => BackupTargetFailureKind.Other,
        };

        var detail = code is null ? response.ReasonPhrase ?? response.StatusCode.ToString() : code + ": " + message;
        throw new BackupTargetException(
            kind,
            "Could not " + action + " (" + (int)response.StatusCode + " " + detail + ").");
    }

    private static (string? Code, string? Message) ParseS3Error(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return (null, null);
        }

        try
        {
            var xml = XDocument.Parse(body);
            return (xml.Root?.Element("Code")?.Value, xml.Root?.Element("Message")?.Value);
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return (null, null);
        }
    }
}
