using System;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace Counterpoint.Backup.Targets;

/// <summary>
/// AWS Signature Version 4, the one scheme every S3-compatible provider this task targets (GCS,
/// R2, B2) accepts, computed by hand rather than pulled in as a dependency of a much larger SDK
/// (P4-T01's own guidance: "keep the footprint minimal").
/// </summary>
/// <remarks>
/// Implements exactly the "s3" service, path-style request signing described in AWS's own
/// documentation ("Signature Calculations for the Authorization Header"). Every value that goes
/// into the signature is computed from the request that is about to be sent, never trusted from
/// the caller, so a request that fails to verify server-side fails because a credential is wrong,
/// not because this signer and the request disagree about what was sent.
/// </remarks>
internal static class AwsSigV4Signer
{
    private const string Algorithm = "AWS4-HMAC-SHA256";
    private const string Service = "s3";
    private const string SignedHeaderNames = "host;x-amz-content-sha256;x-amz-date";

    /// <summary>Signs <paramref name="request"/> in place, adding the headers a request needs to authenticate.</summary>
    /// <param name="payload">The exact bytes that will be sent as the body - empty for GET/DELETE/LIST.</param>
    internal static void Sign(
        HttpRequestMessage request,
        string region,
        string accessKeyId,
        string secretAccessKey,
        byte[] payload,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RequestUri);
        ArgumentNullException.ThrowIfNull(payload);

        var amzDate = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var payloadHash = HexLower(SHA256.HashData(payload));

        var uri = request.RequestUri;
        var host = uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port.ToString(CultureInfo.InvariantCulture);

        var canonicalHeaders = "host:" + host + "\n"
            + "x-amz-content-sha256:" + payloadHash + "\n"
            + "x-amz-date:" + amzDate + "\n";

        var canonicalRequest = request.Method.Method + "\n"
            + CanonicalUri(uri.AbsolutePath) + "\n"
            + CanonicalQueryString(uri.Query) + "\n"
            + canonicalHeaders + "\n"
            + SignedHeaderNames + "\n"
            + payloadHash;

        var credentialScope = dateStamp + "/" + region + "/" + Service + "/aws4_request";
        var stringToSign = Algorithm + "\n"
            + amzDate + "\n"
            + credentialScope + "\n"
            + HexLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));

        var signingKey = DeriveSigningKey(secretAccessKey, dateStamp, region);
        var signature = HexLower(Hmac(signingKey, stringToSign));

        var authorization = Algorithm + " Credential=" + accessKeyId + "/" + credentialScope
            + ", SignedHeaders=" + SignedHeaderNames + ", Signature=" + signature;

        request.Headers.Remove("x-amz-date");
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.Remove("x-amz-content-sha256");
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.Remove("Authorization");
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        request.Headers.Host = host;
    }

    private static byte[] DeriveSigningKey(string secretAccessKey, string dateStamp, string region)
    {
        var kSecret = Encoding.UTF8.GetBytes("AWS4" + secretAccessKey);
        var kDate = Hmac(kSecret, dateStamp);
        var kRegion = Hmac(kDate, region);
        var kService = Hmac(kRegion, Service);
        return Hmac(kService, "aws4_request");
    }

    private static byte[] Hmac(byte[] key, string value)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
    }

    private static string HexLower(byte[] bytes) => Convert.ToHexStringLower(bytes);

    /// <summary>
    /// The request path, percent-encoded per AWS's rules: every octet unreserved
    /// (<c>A-Za-z0-9-_.~</c>) left alone, every other octet as an uppercase-hex <c>%XX</c>
    /// triplet, and <c>/</c> never encoded.
    /// </summary>
    internal static string CanonicalUri(string absolutePath) =>
        string.IsNullOrEmpty(absolutePath) ? "/" : UriEncode(absolutePath, encodeSlash: false);

    /// <summary>Query parameters, each percent-encoded, sorted by key ordinally, joined with <c>&amp;</c>.</summary>
    internal static string CanonicalQueryString(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            return string.Empty;
        }

        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var pairs = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var encoded = new string[pairs.Length];

        for (var i = 0; i < pairs.Length; i++)
        {
            var separator = pairs[i].IndexOf('=');
            var rawKey = separator < 0 ? pairs[i] : pairs[i][..separator];
            var rawValue = separator < 0 ? string.Empty : pairs[i][(separator + 1)..];

            encoded[i] = UriEncode(Uri.UnescapeDataString(rawKey), encodeSlash: true)
                + "=" + UriEncode(Uri.UnescapeDataString(rawValue), encodeSlash: true);
        }

        Array.Sort(encoded, StringComparer.Ordinal);
        return string.Join('&', encoded);
    }

    /// <summary>AWS's own flavour of RFC 3986 percent-encoding - see the AWS SigV4 reference for exactly these rules.</summary>
    internal static string UriEncode(string value, bool encodeSlash)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            var c = (char)b;
            if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_' or '.' or '~')
            {
                builder.Append(c);
            }
            else if (c == '/' && !encodeSlash)
            {
                builder.Append(c);
            }
            else
            {
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }
}
