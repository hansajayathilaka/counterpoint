using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Collections.Generic;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Backup.Targets;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup.Targets;

/// <summary>
/// <see cref="GoogleDriveTarget"/> against a fake <see cref="HttpMessageHandler"/> - no real
/// Google account is needed to prove the refresh-token exchange, the upload/list/download/delete
/// address resolution, and the distinct "authorisation expired" state the task's own risk note
/// asks for (P4-T01).
/// </summary>
public sealed class GoogleDriveTargetTests
{
    private static readonly GoogleDriveCredential Credential = new(
        RefreshToken: "1//fake-refresh-token",
        ClientId: "example.apps.googleusercontent.com",
        ClientSecret: "fake-client-secret",
        FolderId: "folder-abc");

    private const string TokenSuccessBody = """{"access_token":"ya29.fake","expires_in":3600,"token_type":"Bearer"}""";

    [Fact]
    public async Task P4_T01_UploadExchangesTheRefreshTokenThenCreatesTheFile()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(Json(TokenSuccessBody))                          // token exchange
            .Enqueue(Json("""{"files":[]}"""))                        // resolve: no existing file
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }); // create
        var target = new GoogleDriveTarget(handler.ToHttpClient(), Credential);

        await target.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("payload")),
            "backup.cpbak",
            new Dictionary<string, string>());

        handler.Requests.Should().HaveCount(3);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("oauth2.googleapis.com/token");
        handler.Requests[2].RequestUri!.ToString().Should().Contain("uploadType=multipart");
    }

    [Fact]
    public async Task P4_T01_ListParsesTheFilesResponseAndAppliesThePrefixClientSide()
    {
        const string filesJson = """
            {"files":[
              {"name":"2026/09/17/backup.cpbak","size":"1234","modifiedTime":"2026-09-17T20:00:00.000Z"},
              {"name":"other.cpbak","size":"1","modifiedTime":"2026-09-17T20:00:00.000Z"}
            ]}
            """;

        var handler = new FakeHttpMessageHandler()
            .Enqueue(Json(TokenSuccessBody))
            .Enqueue(Json(filesJson));
        var target = new GoogleDriveTarget(handler.ToHttpClient(), Credential);

        var listed = await target.ListAsync("2026/");

        listed.Should().ContainSingle();
        listed[0].Key.Should().Be("2026/09/17/backup.cpbak");
        listed[0].SizeBytes.Should().Be(1234);
    }

    [Fact]
    public async Task P4_T01_DownloadResolvesTheIdThenReadsTheMedia()
    {
        var bytes = Encoding.UTF8.GetBytes("the object's content");
        var handler = new FakeHttpMessageHandler()
            .Enqueue(Json(TokenSuccessBody))
            .Enqueue(Json("""{"files":[{"id":"file123"}]}"""))
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var target = new GoogleDriveTarget(handler.ToHttpClient(), Credential);

        await using var stream = await target.DownloadAsync("backup.cpbak");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(bytes);
        handler.Requests[2].RequestUri!.ToString().Should().Contain("/files/file123");
        handler.Requests[2].RequestUri!.ToString().Should().Contain("alt=media");
    }

    [Fact]
    public async Task P4_T01_AnExpiredRefreshTokenIsSurfacedAsAuthorisationExpiredNotAGenericFailure()
    {
        const string invalidGrant = """{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""";

        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(invalidGrant) });
        var target = new GoogleDriveTarget(handler.ToHttpClient(), Credential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(
            BackupTargetFailureKind.AuthorisationExpired,
            "P4-T01's own stated risk: an expired OAuth grant must not look like a generic upload failure");
        result.Message.Should().ContainEquivalentOf("reconnect");
    }

    [Fact]
    public async Task P4_T01_AForbiddenResponseIsCredentialRejectedNamingTheProblem()
    {
        const string forbidden = """{"error":{"code":403,"message":"The user does not have sufficient permissions for this file."}}""";

        var handler = new FakeHttpMessageHandler()
            .Enqueue(Json(TokenSuccessBody))
            .Enqueue(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(forbidden) });
        var target = new GoogleDriveTarget(handler.ToHttpClient(), Credential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(BackupTargetFailureKind.CredentialRejected);
        result.Message.Should().Contain("sufficient permissions");
    }

    [Fact]
    public async Task P4_T01_ANetworkFailureDuringTheTokenExchangeIsNetworkUnavailable()
    {
        var handler = new FakeHttpMessageHandler()
            .EnqueueThrow(new HttpRequestException("Temporary failure in name resolution"));
        var target = new GoogleDriveTarget(handler.ToHttpClient(), Credential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(BackupTargetFailureKind.NetworkUnavailable);
    }

    [Fact]
    public async Task P4_T01_TestConnectionSucceedsWhenTheTokenAndTheAboutCallBothWork()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(Json(TokenSuccessBody))
            .Enqueue(Json("""{"user":{"emailAddress":"shop@example.test"}}"""));
        var target = new GoogleDriveTarget(handler.ToHttpClient(), Credential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeTrue();
        result.FailureKind.Should().BeNull();
    }

    [Fact]
    public void P4_T01_CredentialRoundTripsThroughJson()
    {
        var json = Credential.ToJson();
        json.Should().Contain("\"refreshToken\"");

        var parsed = GoogleDriveCredential.Parse(json);

        parsed.Should().Be(Credential);
    }

    [Fact]
    public void P4_T01_AMissingFieldIsRejectedAsAFormatException()
    {
        var incomplete = () => GoogleDriveCredential.Parse("""{"clientId":"x"}""");

        incomplete.Should().Throw<FormatException>();
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
