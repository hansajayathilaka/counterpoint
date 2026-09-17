using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// Google Drive, reached through the Drive v3 REST API with a refresh token exchanged for a
/// short-lived access token on demand (SRS FR-11.5, Q-D, P4-T01).
/// </summary>
/// <remarks>
/// <para>
/// Drive has no native notion of an S3-style key: a file is identified by an opaque id, and two
/// files can share a name. This target treats <c>name</c> within the configured folder as the
/// key, resolving it to an id before every read, update or delete - the same address space an
/// S3-compatible bucket already presents to the rest of the backup pipeline.
/// </para>
/// <para>
/// <b>Refresh token in, access token never stored.</b> Every call exchanges the refresh token for
/// a short-lived access token if the cached one has expired; nothing but the refresh token itself
/// (already in the protected credential store) survives past this object's lifetime.
/// </para>
/// <para>
/// <b>An expired or revoked refresh token is reported distinctly</b> (SRS P4-T01's own stated
/// risk): Google's token endpoint answers <c>invalid_grant</c> for exactly that case, which this
/// class surfaces as <see cref="BackupTargetFailureKind.AuthorisationExpired"/> rather than a
/// generic failure, so the settings screen can say "re-connect" instead of "try again".
/// </para>
/// </remarks>
internal sealed class GoogleDriveTarget : IBackupTarget
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string FilesEndpoint = "https://www.googleapis.com/drive/v3/files";
    private const string UploadEndpoint = "https://www.googleapis.com/upload/drive/v3/files";

    private readonly HttpClient _httpClient;
    private readonly GoogleDriveCredential _credential;
    private readonly TimeProvider _timeProvider;

    private string? _accessToken;
    private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;

    internal GoogleDriveTarget(HttpClient httpClient, GoogleDriveCredential credential, TimeProvider? timeProvider = null)
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

        var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var existingId = await ResolveFileIdAsync(key, accessToken, cancellationToken).ConfigureAwait(false);

        if (existingId is not null)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Patch, UploadEndpoint + "/" + existingId + "?uploadType=media");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = new ByteArrayContent(payload);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "upload the backup", cancellationToken).ConfigureAwait(false);
            return;
        }

        var metadataJson = JsonSerializer.Serialize(new
        {
            name = key,
            parents = string.IsNullOrWhiteSpace(_credential.FolderId) ? (string[]?)null : [_credential.FolderId],
        });

        using var multipart = new MultipartContent("related", "cp-" + Guid.NewGuid().ToString("N"));
        multipart.Add(new StringContent(metadataJson, Encoding.UTF8, "application/json"));
        var mediaPart = new ByteArrayContent(payload);
        mediaPart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(mediaPart);

        using var createRequest = new HttpRequestMessage(HttpMethod.Post, UploadEndpoint + "?uploadType=multipart");
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        createRequest.Content = multipart;

        using var createResponse = await SendAsync(createRequest, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(createResponse, "upload the backup", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BackupObjectInfo>> ListAsync(
        string prefix,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var results = new List<BackupObjectInfo>();
        string? pageToken = null;

        do
        {
            var query = "q=" + Uri.EscapeDataString(FolderQuery())
                + "&fields=" + Uri.EscapeDataString("nextPageToken,files(name,size,modifiedTime)")
                + "&pageSize=1000"
                + (pageToken is null ? string.Empty : "&pageToken=" + Uri.EscapeDataString(pageToken));

            using var request = new HttpRequestMessage(HttpMethod.Get, FilesEndpoint + "?" + query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "list the off-site backups", cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (document.RootElement.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    var name = file.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                    if (!name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var size = file.TryGetProperty("size", out var sizeElement)
                        && long.TryParse(sizeElement.GetString(), out var parsedSize)
                        ? parsedSize
                        : 0L;

                    var modified = file.TryGetProperty("modifiedTime", out var modifiedElement)
                        && DateTimeOffset.TryParse(modifiedElement.GetString(), out var parsedDate)
                        ? parsedDate
                        : DateTimeOffset.MinValue;

                    results.Add(new BackupObjectInfo(name, size, modified));
                }
            }

            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var tokenElement)
                ? tokenElement.GetString()
                : null;
        }
        while (pageToken is not null);

        return results;
    }

    /// <inheritdoc />
    public async Task<Stream> DownloadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var fileId = await ResolveFileIdAsync(key, accessToken, cancellationToken).ConfigureAwait(false)
            ?? throw new BackupTargetException(BackupTargetFailureKind.Other, "No backup object exists at '" + key + "'.");

        using var request = new HttpRequestMessage(HttpMethod.Get, FilesEndpoint + "/" + fileId + "?alt=media");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "download the backup", cancellationToken).ConfigureAwait(false);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        var fileId = await ResolveFileIdAsync(key, accessToken, cancellationToken).ConfigureAwait(false);
        if (fileId is null)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Delete, FilesEndpoint + "/" + fileId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "remove the backup", cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BackupTargetConnectionResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await EnsureAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=user");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
            await EnsureSuccessAsync(response, "connect", cancellationToken).ConfigureAwait(false);

            return BackupTargetConnectionResult.Ok("Connected to Google Drive.");
        }
        catch (BackupTargetException ex)
        {
            return BackupTargetConnectionResult.Failed(ex.Message, ex.Kind);
        }
    }

    /// <summary>Exchanges the refresh token for an access token, reusing a still-valid cached one.</summary>
    private async Task<string> EnsureAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_accessToken is not null && _timeProvider.GetUtcNow() < _accessTokenExpiresAt)
        {
            return _accessToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _credential.ClientId,
                ["client_secret"] = _credential.ClientSecret,
                ["refresh_token"] = _credential.RefreshToken,
                ["grant_type"] = "refresh_token",
            }),
        };

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryReadJsonProperty(body, "error");
            var kind = error == "invalid_grant"
                ? BackupTargetFailureKind.AuthorisationExpired
                : BackupTargetFailureKind.CredentialRejected;

            throw new BackupTargetException(
                kind,
                kind == BackupTargetFailureKind.AuthorisationExpired
                    ? "Google Drive authorisation has expired. Reconnect the Drive account in settings."
                    : "Google Drive rejected the stored credential: " + (TryReadJsonProperty(body, "error_description") ?? error ?? "unknown error") + ".");
        }

        using var document = JsonDocument.Parse(body);
        var accessToken = document.RootElement.GetProperty("access_token").GetString()
            ?? throw new BackupTargetException(BackupTargetFailureKind.Other, "Google did not return an access token.");
        var expiresInSeconds = document.RootElement.TryGetProperty("expires_in", out var expiresElement)
            ? expiresElement.GetInt32()
            : 3600;

        _accessToken = accessToken;

        // A minute of slack so a call that starts just before expiry does not fail mid-flight.
        _accessTokenExpiresAt = _timeProvider.GetUtcNow().AddSeconds(Math.Max(0, expiresInSeconds - 60));

        return accessToken;
    }

    private async Task<string?> ResolveFileIdAsync(string key, string accessToken, CancellationToken cancellationToken)
    {
        var query = "q=" + Uri.EscapeDataString(FolderQuery() + " and name = '" + EscapeForQuery(key) + "'")
            + "&fields=" + Uri.EscapeDataString("files(id)") + "&pageSize=1";

        using var request = new HttpRequestMessage(HttpMethod.Get, FilesEndpoint + "?" + query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, "look up the backup", cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

        if (document.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
        {
            return files[0].GetProperty("id").GetString();
        }

        return null;
    }

    private string FolderQuery() => string.IsNullOrWhiteSpace(_credential.FolderId)
        ? "trashed = false"
        : "trashed = false and '" + EscapeForQuery(_credential.FolderId) + "' in parents";

    private static string EscapeForQuery(string value) => value.Replace("'", "\\'", StringComparison.Ordinal);

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.NetworkUnavailable, "Could not reach Google Drive: " + ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BackupTargetException(
                BackupTargetFailureKind.NetworkUnavailable, "The connection to Google Drive timed out.", ex);
        }
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var message = TryReadDriveErrorMessage(body) ?? response.ReasonPhrase ?? response.StatusCode.ToString();

        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => BackupTargetFailureKind.AuthorisationExpired,
            HttpStatusCode.Forbidden => BackupTargetFailureKind.CredentialRejected,
            _ => BackupTargetFailureKind.Other,
        };

        throw new BackupTargetException(
            kind, "Could not " + action + " (" + (int)response.StatusCode + " " + message + ").");
    }

    private static string? TryReadDriveErrorMessage(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var messageElement)
                ? messageElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryReadJsonProperty(string body, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty(propertyName, out var element) ? element.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
