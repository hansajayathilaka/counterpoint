using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Application.Abstractions.Security;
using Counterpoint.Application.Settings;
using Counterpoint.Backup.Targets;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup.Targets;

/// <summary>
/// P4-T01's own "Done when": "no secret appears in any config file or log." A settings screen (or
/// a log line built from an unhandled exception) shows <see cref="Exception.Message"/> and
/// <see cref="BackupTargetConnectionResult.Message"/>, so those are exactly the surfaces this
/// class drives through every failure path of <see cref="S3CompatibleTarget"/> and
/// <see cref="GoogleDriveTarget"/> - a rejected credential, a malformed stored credential, an
/// expired OAuth grant, and a network failure - asserting the shop's own secret values (marked
/// with a unique, greppable string each so a leak cannot hide inside a longer legitimate-looking
/// message) never appear in the message an owner or a log would see.
/// </summary>
public sealed class BackupTargetErrorMessagesNeverLeakSecretsTests
{
    // Deliberately distinctive strings, so an assertion failure names exactly which secret leaked
    // rather than a coincidental substring match.
    private const string S3AccessKeyMarker = "AKIA-SECRET-MARKER-ACCESS-KEY";
    private const string S3SecretKeyMarker = "SECRET-MARKER-WJALR-WOULD-NEVER-APPEAR";
    private const string DriveRefreshTokenMarker = "SECRET-MARKER-REFRESH-TOKEN-WOULD-NEVER-APPEAR";
    private const string DriveClientSecretMarker = "SECRET-MARKER-CLIENT-SECRET-WOULD-NEVER-APPEAR";

    private static readonly S3Credential S3Credential = new(
        Endpoint: "https://storage.example.test",
        Region: "auto",
        Bucket: "shop-backups",
        AccessKeyId: S3AccessKeyMarker,
        SecretAccessKey: S3SecretKeyMarker);

    private static readonly GoogleDriveCredential DriveCredential = new(
        RefreshToken: DriveRefreshTokenMarker,
        ClientId: "example.apps.googleusercontent.com",
        ClientSecret: DriveClientSecretMarker,
        FolderId: "folder-abc");

    // --- S3-compatible -----------------------------------------------------------------------

    [Fact]
    public async Task P4_T01_ARejectedS3CredentialsExceptionMessageNeverContainsTheAccessKeyOrSecretKey()
    {
        const string errorXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Error>
              <Code>SignatureDoesNotMatch</Code>
              <Message>The request signature does not match.</Message>
            </Error>
            """;
        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(errorXml) });
        var target = new S3CompatibleTarget(handler.ToHttpClient(), S3Credential);

        var upload = () => target.UploadAsync(
            new MemoryStream([1, 2, 3]), "key.cpbak", new Dictionary<string, string>());

        var thrown = await upload.Should().ThrowAsync<BackupTargetException>();
        AssertNoSecretLeaked(thrown.Which);
    }

    [Fact]
    public async Task P4_T01_ANS3TestConnectionFailureMessageNeverContainsTheAccessKeyOrSecretKey()
    {
        const string errorXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Error>
              <Code>InvalidAccessKeyId</Code>
              <Message>The access key id you provided does not exist.</Message>
            </Error>
            """;
        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(errorXml) });
        var target = new S3CompatibleTarget(handler.ToHttpClient(), S3Credential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        AssertNoSecretLeaked(result.Message);
    }

    [Fact]
    public async Task P4_T01_AnS3NetworkFailureMessageNeverContainsTheAccessKeyOrSecretKey()
    {
        var handler = new FakeHttpMessageHandler()
            .EnqueueThrow(new HttpRequestException("Name or service not known"));
        var target = new S3CompatibleTarget(handler.ToHttpClient(), S3Credential);

        var download = () => target.DownloadAsync("key.cpbak");

        var thrown = await download.Should().ThrowAsync<BackupTargetException>();
        AssertNoSecretLeaked(thrown.Which);
    }

    [Fact]
    public void P4_T01_AMalformedStoredS3CredentialsFormatExceptionNeverEchoesTheOriginalText()
    {
        // A stored credential that is almost valid JSON but truncated - the kind of corruption a
        // partially-written settings file could produce - with the real secret sitting right there
        // in the truncated text. BackupTargetFactory.Create must not let any of that leak into the
        // ConfigurationError it raises.
        var handler = new FakeHttpMessageHandler();
        var credentials = new InMemoryCredentialStore();
        credentials.SetCredential(
            BackupTargetCredentialKey.S3Compatible,
            $$"""{"endpoint":"https://x","bucket":"b","accessKeyId":"{{S3AccessKeyMarker}}","secretAccessKey":"{{S3SecretKeyMarker}}""");
        var factory = new BackupTargetFactory(handler.ToHttpClient(), credentials);

        var create = () => factory.Create(CloudBackupTarget.S3Compatible);

        var thrown = create.Should().Throw<BackupTargetException>();
        AssertNoSecretLeaked(thrown.Which);
    }

    // --- Google Drive -------------------------------------------------------------------------

    [Fact]
    public async Task P4_T01_AnExpiredDriveGrantsMessageNeverContainsTheRefreshTokenOrClientSecret()
    {
        const string invalidGrant = """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""";
        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(invalidGrant) });
        var target = new GoogleDriveTarget(handler.ToHttpClient(), DriveCredential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        AssertNoSecretLeaked(result.Message);
    }

    [Fact]
    public async Task P4_T01_ADriveTokenExchangeRejectionMessageNeverContainsTheRefreshTokenOrClientSecret()
    {
        // A non-invalid_grant rejection of the token exchange itself (e.g. a revoked client) - the
        // CredentialRejected branch, which is built from the server's own body, not the request.
        const string invalidClient = """{"error":"invalid_client","error_description":"The OAuth client was not found."}""";
        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(invalidClient) });
        var target = new GoogleDriveTarget(handler.ToHttpClient(), DriveCredential);

        var download = () => target.DownloadAsync("key.cpbak");

        var thrown = await download.Should().ThrowAsync<BackupTargetException>();
        AssertNoSecretLeaked(thrown.Which);
    }

    [Fact]
    public async Task P4_T01_ADriveApiRejectionAfterASuccessfulTokenExchangeNeverContainsTheRefreshTokenOrClientSecret()
    {
        const string tokenSuccess = """{"access_token":"ya29.fake","expires_in":3600,"token_type":"Bearer"}""";
        const string forbidden = """{"error":{"code":403,"message":"The user does not have sufficient permissions for this file."}}""";
        var handler = new FakeHttpMessageHandler()
            .Enqueue(Json(tokenSuccess))
            .Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(forbidden) });
        var target = new GoogleDriveTarget(handler.ToHttpClient(), DriveCredential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        AssertNoSecretLeaked(result.Message);
    }

    [Fact]
    public async Task P4_T01_ADriveNetworkFailureDuringTheTokenExchangeNeverContainsTheRefreshTokenOrClientSecret()
    {
        var handler = new FakeHttpMessageHandler()
            .EnqueueThrow(new HttpRequestException("Temporary failure in name resolution"));
        var target = new GoogleDriveTarget(handler.ToHttpClient(), DriveCredential);

        var upload = () => target.UploadAsync(
            new MemoryStream([1]), "key.cpbak", new Dictionary<string, string>());

        var thrown = await upload.Should().ThrowAsync<BackupTargetException>();
        AssertNoSecretLeaked(thrown.Which);
    }

    [Fact]
    public void P4_T01_AMalformedStoredDriveCredentialsFormatExceptionNeverEchoesTheOriginalText()
    {
        var handler = new FakeHttpMessageHandler();
        var credentials = new InMemoryCredentialStore();
        credentials.SetCredential(
            BackupTargetCredentialKey.GoogleDrive,
            $$"""{"refreshToken":"{{DriveRefreshTokenMarker}}","clientId":"x","clientSecret":"{{DriveClientSecretMarker}}""");
        var factory = new BackupTargetFactory(handler.ToHttpClient(), credentials);

        var create = () => factory.Create(CloudBackupTarget.GoogleDrive);

        var thrown = create.Should().Throw<BackupTargetException>();
        AssertNoSecretLeaked(thrown.Which);
    }

    private static void AssertNoSecretLeaked(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            AssertNoSecretLeaked(current.Message);
        }
    }

    private static void AssertNoSecretLeaked(string message)
    {
        message.Should().NotContain(S3AccessKeyMarker);
        message.Should().NotContain(S3SecretKeyMarker);
        message.Should().NotContain(DriveRefreshTokenMarker);
        message.Should().NotContain(DriveClientSecretMarker);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    /// <summary>A fake protected store: an in-memory dictionary, standing in for Credential Manager or the development file store.</summary>
    private sealed class InMemoryCredentialStore : IBackupTargetCredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool HasCredential(string targetKey) => _values.ContainsKey(targetKey);

        public void SetCredential(string targetKey, string credential) => _values[targetKey] = credential;

        public void RemoveCredential(string targetKey) => _values.Remove(targetKey);

        public string? TryGetCredential(string targetKey) => _values.GetValueOrDefault(targetKey);
    }
}
