using System;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Counterpoint.Application.Security;

/// <summary>
/// Builds the <c>before_json</c> / <c>after_json</c> payloads the security audit rows carry.
/// </summary>
/// <remarks>
/// <para>
/// Written by hand rather than serialised from an object, for the same reason
/// <c>CompleteSaleHandler</c> writes its own: the text is about to be hashed into the
/// <c>audit_log</c> chain (CLAUDE.md invariant 6), so the byte-for-byte output has to be a
/// property of this code and not of a serialiser's defaults, its member ordering or its version.
/// </para>
/// <para>
/// String values go through <see cref="JsonEncodedText.Encode(string, System.Text.Encodings.Web.JavaScriptEncoder?)"/>,
/// so a username containing a quote produces valid JSON rather than a broken row.
/// </para>
/// <para>
/// <b>Nothing secret is ever passed in here.</b> Not a password, not a PIN, not a hash. The
/// engineering guide's "never log" list applies to the audit trail before it applies to anything
/// else, because unlike the log the audit trail cannot be deleted.
/// </para>
/// </remarks>
internal static class SecurityAuditJson
{
    /// <summary>Builds a flat JSON object from the fields given, in the order given.</summary>
    internal static string Object(params (string Name, AuditValue Value)[] fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var builder = new StringBuilder("{");

        for (var i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder
                .Append('"')
                .Append(JsonEncodedText.Encode(fields[i].Name))
                .Append("\":")
                .Append(fields[i].Value.ToJson());
        }

        return builder.Append('}').ToString();
    }

    /// <summary>
    /// One JSON value: a quoted string, a number, a boolean or null. Converting implicitly keeps
    /// the call sites readable while still deciding quoting by type rather than by guesswork.
    /// </summary>
    internal readonly struct AuditValue
    {
        private readonly string? _text;
        private readonly string? _literal;

        private AuditValue(string? text, string? literal)
        {
            _text = text;
            _literal = literal;
        }

        public static implicit operator AuditValue(string? value) => new(value, null);

        public static implicit operator AuditValue(long value) =>
            new(null, value.ToString(CultureInfo.InvariantCulture));

        public static implicit operator AuditValue(bool value) => new(null, value ? "true" : "false");

        /// <summary>Named alternative to the implicit string conversion (CA2225).</summary>
        public static AuditValue FromString(string? value) => value;

        /// <summary>Named alternative to the implicit integer conversion (CA2225).</summary>
        public static AuditValue FromInt64(long value) => value;

        /// <summary>Named alternative to the implicit boolean conversion (CA2225).</summary>
        public static AuditValue FromBoolean(bool value) => value;

        internal string ToJson() => _literal ?? (_text is null
            ? "null"
            : string.Concat("\"", JsonEncodedText.Encode(_text).ToString(), "\""));
    }
}
