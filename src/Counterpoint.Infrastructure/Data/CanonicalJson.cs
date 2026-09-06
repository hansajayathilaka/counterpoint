using System;
using System.Globalization;
using System.Text;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Builds the one canonical JSON form of a row that <see cref="HashChain"/> hashes.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic by construction, not by convention. The caller names each field in order, so
/// the field order is a declaration in source that a reviewer can read against the DDL - never
/// reflection order, which a property move would silently change and every previously written
/// hash would then fail to verify.
/// </para>
/// <para>
/// The rules, all of them:
/// </para>
/// <list type="bullet">
///   <item>An object, fields in the order they were added, no whitespace at all.</item>
///   <item>Strings double-quoted, with <c>"</c>, <c>\</c> and every character below U+0020
///   escaped; the shortest escape where JSON defines one, <c>\u00XX</c> otherwise.</item>
///   <item>Money, quantities and rates as the scaled integers the database stores - the same
///   digits, so a hash can be recomputed from a raw SQL dump.</item>
///   <item>Timestamps as the fixed-width ISO-8601 text the column holds, character for
///   character (<see cref="Iso8601TimestampConverter.Format"/>).</item>
///   <item>Null as <c>null</c>, explicitly. A missing field and a null field must not produce
///   the same bytes.</item>
///   <item>Invariant culture everywhere. A till configured for a comma decimal separator must
///   produce the same hash as one that is not.</item>
/// </list>
/// </remarks>
public sealed class CanonicalJson
{
    private readonly StringBuilder _builder = new StringBuilder("{");
    private bool _hasFields;

    /// <summary>Adds a string field, or <c>null</c>.</summary>
    public CanonicalJson Add(string name, string? value)
    {
        StartField(name);

        if (value is null)
        {
            _builder.Append("null");
        }
        else
        {
            AppendQuoted(value);
        }

        return this;
    }

    /// <summary>Adds an integer field, or <c>null</c>. Counts and ids, not money.</summary>
    public CanonicalJson Add(string name, long? value)
    {
        StartField(name);
        _builder.Append(value is null
            ? "null"
            : value.Value.ToString(CultureInfo.InvariantCulture));

        return this;
    }

    /// <summary>Adds a money field as its scaled integer.</summary>
    public CanonicalJson Add(string name, Money value) =>
        Add(name, value.ToScaled());

    /// <summary>Adds a rate field as its scaled integer.</summary>
    public CanonicalJson Add(string name, TaxRate value) =>
        Add(name, value.ToScaled());

    /// <summary>Adds a timestamp field, or <c>null</c>, exactly as the column stores it.</summary>
    public CanonicalJson Add(string name, DateTimeOffset? value)
    {
        StartField(name);

        if (value is null)
        {
            _builder.Append("null");
        }
        else
        {
            AppendQuoted(value.Value.ToString(Iso8601TimestampConverter.Format, CultureInfo.InvariantCulture));
        }

        return this;
    }

    /// <summary>Adds a boolean field as <c>0</c> or <c>1</c>, which is how it is stored.</summary>
    public CanonicalJson Add(string name, bool value) =>
        Add(name, value ? 1L : 0L);

    /// <summary>The finished canonical form.</summary>
    public override string ToString() => _builder.ToString() + "}";

    private void StartField(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (_hasFields)
        {
            _builder.Append(',');
        }

        _hasFields = true;
        AppendQuoted(name);
        _builder.Append(':');
    }

    private void AppendQuoted(string value)
    {
        _builder.Append('"');

        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    _builder.Append("\\\"");
                    break;
                case '\\':
                    _builder.Append("\\\\");
                    break;
                case '\b':
                    _builder.Append("\\b");
                    break;
                case '\f':
                    _builder.Append("\\f");
                    break;
                case '\n':
                    _builder.Append("\\n");
                    break;
                case '\r':
                    _builder.Append("\\r");
                    break;
                case '\t':
                    _builder.Append("\\t");
                    break;
                default:
                    if (character < ' ')
                    {
                        _builder.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}");
                    }
                    else
                    {
                        _builder.Append(character);
                    }

                    break;
            }
        }

        _builder.Append('"');
    }
}
