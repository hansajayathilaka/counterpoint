using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// One variant's attribute set - <c>product_variant.attributes</c>, for example
/// <c>{"length":"50mm","thread":"M8","finish":"Zinc"}</c> - compared by value rather than by key
/// order, so <see cref="VariantMatrixGenerator"/> can tell "already exists" from "new" no matter
/// which order the axes or the stored JSON happen to list their keys in
/// (docs/01_DATA_MODEL.md §3, SRS FR-2.6).
/// </summary>
public sealed class VariantAttributes : IEquatable<VariantAttributes>
{
    private readonly (string Key, string Value)[] _pairs;

    /// <exception cref="ArgumentException"><paramref name="values"/> is empty, or a key or value is blank.</exception>
    public VariantAttributes(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (values.Count == 0)
        {
            throw new ArgumentException("A variant needs at least one attribute.", nameof(values));
        }

        _pairs = values
            .Select(pair => (Key: RequireNotBlank(pair.Key, "attribute name"), Value: RequireNotBlank(pair.Value, "attribute value")))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>The attribute names and values, in canonical (sorted) order.</summary>
    public IReadOnlyList<(string Key, string Value)> Pairs => _pairs;

    /// <summary>As a plain dictionary, for serialising onto <c>product_variant.attributes</c>.</summary>
    public IReadOnlyDictionary<string, string> ToDictionary() =>
        _pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    /// <inheritdoc />
    public bool Equals(VariantAttributes? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (_pairs.Length != other._pairs.Length)
        {
            return false;
        }

        for (var i = 0; i < _pairs.Length; i++)
        {
            if (!string.Equals(_pairs[i].Key, other._pairs[i].Key, StringComparison.Ordinal) ||
                !string.Equals(_pairs[i].Value, other._pairs[i].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as VariantAttributes);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);

        foreach (var (key, value) in _pairs)
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <summary>Culture-invariant, for logs, tests and the matrix preview - not for a receipt.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();

        for (var i = 0; i < _pairs.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(_pairs[i].Key).Append('=').Append(_pairs[i].Value);
        }

        return builder.ToString();
    }

    private static string RequireNotBlank(string value, string what)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"A variant's {what} cannot be blank."), nameof(value));
        }

        return value.Trim();
    }
}
