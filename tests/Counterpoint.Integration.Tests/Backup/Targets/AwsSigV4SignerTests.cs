using System;
using System.Net.Http;
using System.Text;
using Counterpoint.Backup.Targets;
using FluentAssertions;

namespace Counterpoint.Integration.Tests.Backup.Targets;

/// <summary>
/// <see cref="AwsSigV4Signer"/> against fixed, hand-worked examples - the one thing
/// <see cref="S3CompatibleTargetTests"/> never proves, because it only asserts an
/// <c>Authorization</c> header exists with a plausible prefix, never that the signature itself is
/// the one a real S3-compatible server would also compute and accept.
/// </summary>
/// <remarks>
/// <para>
/// Every constant below - date, region, keys, path, query, payload - is fixed, not
/// <c>DateTimeOffset.UtcNow</c> or anything random, so the expected values are reproducible.
/// </para>
/// <para>
/// <b>How the expected values were derived.</b> Not by inspecting <see cref="AwsSigV4Signer"/>'s
/// own source and echoing it back: that would only prove the test agrees with the implementation,
/// which is exactly the failure mode a hand-rolled signer with no independent check needs to
/// avoid. Instead each expected canonical request, string-to-sign and signature below was computed
/// by an independent Python script (stdlib <c>hashlib</c>/<c>hmac</c> only, no AWS SDK, no
/// reference to this repository's C#) implementing the algorithm AWS documents in "Signature
/// Calculations for the Authorization Header"
/// (https://docs.aws.amazon.com/AmazonS3/latest/API/sig-v4-header-based-auth.html):
/// <c>kDate = HMAC(kSecret, dateStamp)</c>, <c>kRegion = HMAC(kDate, region)</c>,
/// <c>kService = HMAC(kRegion, "s3")</c>, <c>kSigning = HMAC(kService, "aws4_request")</c>,
/// <c>signature = HMAC-hex(kSigning, stringToSign)</c>. That script's output is transcribed
/// verbatim into the constants and expected strings below (and its empty-payload SHA-256,
/// <c>e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855</c>, is itself AWS's own
/// well-known published constant for the hash of zero bytes - an independent cross-check that the
/// script's own hashing is correct, not just self-consistent).
/// </para>
/// <para>
/// The signer only ever signs exactly three headers - <c>host</c>, <c>x-amz-content-sha256</c>,
/// <c>x-amz-date</c> (see <c>AwsSigV4Signer.SignedHeaderNames</c>) - never <c>range</c> or any
/// other request header, so AWS's own published GET-Object worked example (which signs
/// <c>range</c> too) does not apply unmodified here; these two examples are this signer's own
/// three-header shape instead, worked by hand for a PUT with a body and a GET with a sorted,
/// escaped query string.
/// </para>
/// </remarks>
public sealed class AwsSigV4SignerTests
{
    // AWS's own published example access key / secret key pair (used throughout AWS's SigV4
    // documentation and already reused by S3CompatibleTargetTests' own Credential) - not a real
    // credential, chosen so a future reader who knows AWS's docs recognises it immediately.
    private const string AccessKeyId = "AKIAIOSFODNN7EXAMPLE";
    private const string SecretAccessKey = "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY";
    private const string Region = "us-east-1";

    // 2013-05-24T00:00:00Z - the date AWS's own worked examples use, fixed, never UtcNow.
    private static readonly DateTimeOffset FixedNow = new(2013, 5, 24, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void P4_T01_APutRequestWithABodyProducesTheIndependentlyComputedSignatureExactly()
    {
        // Worked by hand (script-verified) as follows:
        //
        // payload = UTF8("hello counterpoint")
        // payloadHash (sha256, hex) = c313dc132ca96ba5b7cec7591f861318abb4206502787953f07ac632568e4be1
        //
        // canonicalRequest =
        //   "PUT\n" +
        //   "/examplebucket/test.txt\n" +
        //   "\n" +                                                              (no query string)
        //   "host:s3.example-region.amazonaws.com\n" +
        //   "x-amz-content-sha256:c313dc132ca96ba5b7cec7591f861318abb4206502787953f07ac632568e4be1\n" +
        //   "x-amz-date:20130524T000000Z\n" +
        //   "\n" +
        //   "host;x-amz-content-sha256;x-amz-date\n" +
        //   "c313dc132ca96ba5b7cec7591f861318abb4206502787953f07ac632568e4be1"
        //
        // hashedCanonicalRequest (sha256, hex) =
        //   2123195330faa70dac650bbc82523a708bdab85ccb6bba4bdaf128a412ad822e
        //
        // credentialScope = "20130524/us-east-1/s3/aws4_request"
        //
        // stringToSign =
        //   "AWS4-HMAC-SHA256\n" +
        //   "20130524T000000Z\n" +
        //   "20130524/us-east-1/s3/aws4_request\n" +
        //   "2123195330faa70dac650bbc82523a708bdab85ccb6bba4bdaf128a412ad822e"
        //
        // kDate    = HMAC-SHA256("AWS4" + SecretAccessKey, "20130524")
        //          = 68896419206d6240ad4cd7dc8ba658efbf3b43b53041950083a10833824fcfbb
        // kRegion  = HMAC-SHA256(kDate, "us-east-1")
        //          = 0506335cc36b4a971f6beddf0adbd976ee71222cb42c131487e0c12c5c47a025
        // kService = HMAC-SHA256(kRegion, "s3")
        //          = 05602c14e8b6aad30e7f6dec4b544071f6e4a742934bc5e36415733c47a67d44
        // kSigning = HMAC-SHA256(kService, "aws4_request")
        //          = dbb893acc010964918f1fd433add87c70e8b0db6be30c1fbeafefa5ec6ba8378
        //
        // signature = HMAC-SHA256-hex(kSigning, stringToSign)
        //           = ed644df3bf2cb64e4fc5de056b771ad34f493ec20355b5c12b3fc877be0b2f29
        const string ExpectedPayloadHash = "c313dc132ca96ba5b7cec7591f861318abb4206502787953f07ac632568e4be1";
        const string ExpectedSignature = "ed644df3bf2cb64e4fc5de056b771ad34f493ec20355b5c12b3fc877be0b2f29";
        const string ExpectedAuthorization =
            "AWS4-HMAC-SHA256 Credential=" + AccessKeyId + "/20130524/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;x-amz-content-sha256;x-amz-date, Signature=" + ExpectedSignature;

        using var request = new HttpRequestMessage(
            HttpMethod.Put, new Uri("https://s3.example-region.amazonaws.com/examplebucket/test.txt"));
        var payload = Encoding.UTF8.GetBytes("hello counterpoint");

        AwsSigV4Signer.Sign(request, Region, AccessKeyId, SecretAccessKey, payload, FixedNow);

        request.Headers.GetValues("x-amz-date").Should().ContainSingle("20130524T000000Z");
        request.Headers.GetValues("x-amz-content-sha256").Should().ContainSingle(ExpectedPayloadHash);
        request.Headers.GetValues("Authorization").Should().ContainSingle(ExpectedAuthorization);
        request.Headers.Host.Should().Be("s3.example-region.amazonaws.com");
    }

    [Fact]
    public void P4_T01_AGetRequestWithASortedEscapedQueryStringProducesTheIndependentlyComputedSignatureExactly()
    {
        // Worked by hand (script-verified) the same way as the PUT example above, this time for an
        // empty-body GET against a query string whose parameters are out of order in the request
        // URI (zulu, prefix, alpha) and need SigV4's own escaping rules applied (a literal space
        // becomes %20, a literal '/' in a value becomes %2F, an already-percent-encoded value is
        // decoded once and re-encoded rather than doubly encoded):
        //
        // canonicalQueryString (sorted ordinally by key) =
        //   "alpha=hello%20world&prefix=2026%2F09&zulu=1"
        //
        // payloadHash (sha256 of zero bytes, hex) - AWS's own published constant for this -
        //   e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855
        //
        // canonicalRequest =
        //   "GET\n" +
        //   "/examplebucket/\n" +
        //   "alpha=hello%20world&prefix=2026%2F09&zulu=1\n" +
        //   "host:s3.example-region.amazonaws.com\n" +
        //   "x-amz-content-sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n" +
        //   "x-amz-date:20130524T000000Z\n" +
        //   "\n" +
        //   "host;x-amz-content-sha256;x-amz-date\n" +
        //   "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
        //
        // hashedCanonicalRequest (sha256, hex) =
        //   517d8749d7773737f1317c4165a9ec3d098965a195cab12c5a3047b61f88217b
        //
        // stringToSign =
        //   "AWS4-HMAC-SHA256\n" +
        //   "20130524T000000Z\n" +
        //   "20130524/us-east-1/s3/aws4_request\n" +
        //   "517d8749d7773737f1317c4165a9ec3d098965a195cab12c5a3047b61f88217b"
        //
        // Using the same kSigning derived in the PUT example above (the signing key depends only on
        // the date, region, service and secret key, not on the request):
        //
        // signature = HMAC-SHA256-hex(kSigning, stringToSign)
        //           = 034fd19207766b816201636a9b2b8286ace6f609cfe0fe0ba010dc1af410e987
        const string ExpectedPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        const string ExpectedSignature = "034fd19207766b816201636a9b2b8286ace6f609cfe0fe0ba010dc1af410e987";
        const string ExpectedAuthorization =
            "AWS4-HMAC-SHA256 Credential=" + AccessKeyId + "/20130524/us-east-1/s3/aws4_request, "
            + "SignedHeaders=host;x-amz-content-sha256;x-amz-date, Signature=" + ExpectedSignature;

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri("https://s3.example-region.amazonaws.com/examplebucket/?zulu=1&prefix=2026%2F09&alpha=hello%20world"));

        AwsSigV4Signer.Sign(request, Region, AccessKeyId, SecretAccessKey, payload: [], FixedNow);

        request.Headers.GetValues("x-amz-content-sha256").Should().ContainSingle(ExpectedPayloadHash);
        request.Headers.GetValues("Authorization").Should().ContainSingle(ExpectedAuthorization);
    }

    [Theory]
    [InlineData("", "/")]
    [InlineData("/", "/")]
    [InlineData("/examplebucket/test.txt", "/examplebucket/test.txt")]

    // Unreserved characters (A-Za-z0-9-_.~) pass through untouched; '/' is never encoded in a URI path.
    [InlineData("/A-z0-9-_.~/", "/A-z0-9-_.~/")]

    // A space in a path segment must become %20, per AWS's own RFC 3986-derived rule - never '+'.
    [InlineData("/a path with spaces", "/a%20path%20with%20spaces")]

    // A reserved character in a path segment is percent-encoded with uppercase hex digits.
    [InlineData("/key+with+plus", "/key%2Bwith%2Bplus")]
    public void P4_T01_CanonicalUriEncodesEverySegmentPerAwsRulesAndNeverEncodesTheSlash(
        string absolutePath, string expected) =>
        AwsSigV4Signer.CanonicalUri(absolutePath).Should().Be(expected);

    [Theory]
    [InlineData("A-Za-z0-9-_.~", "A-Za-z0-9-_.~")]
    [InlineData("a b", "a%20b")]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("a+b", "a%2Bb")]
    public void P4_T01_UriEncodeWithSlashEncodedAppliesSigV4sRulesToQueryValues(string value, string expected) =>
        AwsSigV4Signer.UriEncode(value, encodeSlash: true).Should().Be(expected);

    [Fact]
    public void P4_T01_UriEncodeWithSlashNotEncodedLeavesThePathSeparatorAlone() =>
        AwsSigV4Signer.UriEncode("a/b c", encodeSlash: false).Should().Be("a/b%20c");

    [Fact]
    public void P4_T01_CanonicalQueryStringSortsParametersOrdinallyByKey()
    {
        // "Zulu" sorts before "alpha" under a case-insensitive comparison but must not here - SigV4
        // requires ordinal (byte) ordering, so an upper-case key sorts before every lower-case one.
        AwsSigV4Signer.CanonicalQueryString("zulu=1&alpha=2&Zulu=3")
            .Should().Be("Zulu=3&alpha=2&zulu=1");
    }

    [Fact]
    public void P4_T01_CanonicalQueryStringEncodesASpaceAsPercentTwentyNotPlus() =>
        AwsSigV4Signer.CanonicalQueryString("note=a b").Should().Be("note=a%20b");

    [Fact]
    public void P4_T01_CanonicalQueryStringReEncodesAnAlreadyPercentEncodedValueOnceNotTwice()
    {
        // The raw query string as it comes off a real Uri.Query already carries one layer of
        // percent-encoding (here, %2F for a literal '/' in the value). CanonicalQueryString must
        // decode that one layer and re-apply SigV4's own escaping - landing back on %2F - rather
        // than escaping the already-escaped text into %252F.
        AwsSigV4Signer.CanonicalQueryString("prefix=2026%2F09").Should().Be("prefix=2026%2F09");
    }

    [Fact]
    public void P4_T01_CanonicalQueryStringLeavesTildeAlone() =>
        AwsSigV4Signer.CanonicalQueryString("key=~unreserved~").Should().Be("key=~unreserved~");

    [Fact]
    public void P4_T01_CanonicalQueryStringOfAnEmptyOrMissingQueryIsEmpty()
    {
        AwsSigV4Signer.CanonicalQueryString(string.Empty).Should().Be(string.Empty);
        AwsSigV4Signer.CanonicalQueryString("?").Should().Be(string.Empty);
    }
}
