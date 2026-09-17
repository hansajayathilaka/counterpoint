using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Backup;
using Counterpoint.Backup.Targets;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup.Targets;

/// <summary>
/// <see cref="S3CompatibleTarget"/> against a fake <see cref="HttpMessageHandler"/> - no real
/// bucket is needed to prove the request is signed, the response is parsed, and a rejected
/// credential is reported by name (P4-T01's own "Done when": "each target uploads, lists and
/// downloads a test file" and "a wrong credential produces a clear message naming the problem").
/// </summary>
public sealed class S3CompatibleTargetTests
{
    private static readonly S3Credential Credential = new(
        Endpoint: "https://storage.example.test",
        Region: "auto",
        Bucket: "shop-backups",
        AccessKeyId: "AKIAEXAMPLE",
        SecretAccessKey: "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY");

    [Fact]
    public async Task P4_T01_UploadSignsTheRequestWithSigV4()
    {
        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK));
        var target = new S3CompatibleTarget(handler.ToHttpClient(), Credential);

        await target.UploadAsync(
            new MemoryStream(Encoding.UTF8.GetBytes("payload")),
            "2026/09/17/backup.cpbak",
            new Dictionary<string, string> { ["schemaVersion"] = "42" });

        handler.Requests.Should().ContainSingle();
        var request = handler.Requests[0];
        request.Method.Should().Be(HttpMethod.Put);
        request.RequestUri!.AbsolutePath.Should().Be("/shop-backups/2026/09/17/backup.cpbak");
        request.Headers.GetValues("Authorization").Should().ContainSingle(
            v => v.StartsWith("AWS4-HMAC-SHA256 Credential=AKIAEXAMPLE/", StringComparison.Ordinal));
        request.Headers.Contains("x-amz-date").Should().BeTrue();
        request.Headers.Contains("x-amz-content-sha256").Should().BeTrue();
        request.Content!.Headers.GetValues("x-amz-meta-schemaVersion").Should().ContainSingle("42");
    }

    [Fact]
    public async Task P4_T01_ListParsesTheS3XmlResponse()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
              <IsTruncated>false</IsTruncated>
              <Contents>
                <Key>2026/09/17/backup.cpbak</Key>
                <Size>1234</Size>
                <LastModified>2026-09-17T20:00:00.000Z</LastModified>
              </Contents>
            </ListBucketResult>
            """;

        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
        var target = new S3CompatibleTarget(handler.ToHttpClient(), Credential);

        var listed = await target.ListAsync("2026/");

        listed.Should().ContainSingle();
        listed[0].Key.Should().Be("2026/09/17/backup.cpbak");
        listed[0].SizeBytes.Should().Be(1234);
    }

    [Fact]
    public async Task P4_T01_DownloadReturnsTheObjectBytes()
    {
        var bytes = Encoding.UTF8.GetBytes("the object's content");
        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        var target = new S3CompatibleTarget(handler.ToHttpClient(), Credential);

        await using var stream = await target.DownloadAsync("backup.cpbak");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);

        buffer.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public async Task P4_T01_AWrongCredentialProducesAClearMessageNamingTheProblem()
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
        var target = new S3CompatibleTarget(handler.ToHttpClient(), Credential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(BackupTargetFailureKind.CredentialRejected);
        result.Message.Should().Contain("SignatureDoesNotMatch");
    }

    [Fact]
    public async Task P4_T01_ANetworkFailureIsReportedAsNetworkUnavailableNotAGenericError()
    {
        var handler = new FakeHttpMessageHandler()
            .EnqueueThrow(new HttpRequestException("Name or service not known"));
        var target = new S3CompatibleTarget(handler.ToHttpClient(), Credential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeFalse();
        result.FailureKind.Should().Be(BackupTargetFailureKind.NetworkUnavailable);
    }

    [Fact]
    public async Task P4_T01_TestConnectionSucceedsWhenTheBucketAnswers()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <ListBucketResult xmlns="http://s3.amazonaws.com/doc/2006-03-01/">
              <IsTruncated>false</IsTruncated>
            </ListBucketResult>
            """;

        var handler = new FakeHttpMessageHandler()
            .Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
        var target = new S3CompatibleTarget(handler.ToHttpClient(), Credential);

        var result = await target.TestConnectionAsync();

        result.Success.Should().BeTrue();
        result.FailureKind.Should().BeNull();
    }

    [Fact]
    public void P4_T01_CredentialRoundTripsThroughJson()
    {
        var json = Credential.ToJson();
        json.Should().Contain("\"secretAccessKey\"");

        var parsed = S3Credential.Parse(json);

        parsed.Should().Be(Credential);
    }

    [Fact]
    public void P4_T01_AMissingFieldIsRejectedAsAFormatExceptionNotSilentlyAccepted()
    {
        var incomplete = () => S3Credential.Parse("""{"endpoint":"https://x"}""");

        incomplete.Should().Throw<FormatException>();
    }
}
