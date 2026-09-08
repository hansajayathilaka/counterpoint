using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Counterpoint.Domain.Catalogue;

namespace Counterpoint.Application.Catalogue;

/// <summary>
/// Builds a readable, deterministic SKU for a matrix-generated variant from the product's code
/// and the combination's attribute values (SRS FR-2.6) - "BOLT-M8-25MM-ZINC" rather than a bare
/// sequence number, so the SKU on a printed label still says what it is.
/// </summary>
internal static class VariantSkuGenerator
{
    /// <summary>
    /// Builds a candidate SKU, disambiguated against <paramref name="taken"/> by appending
    /// <c>-2</c>, <c>-3</c>, ... if the plain candidate is already in use. Adds the chosen SKU to
    /// <paramref name="taken"/> before returning, so the next call in the same batch sees it.
    /// </summary>
    internal static string Generate(string productCode, VariantAttributes attributes, HashSet<string> taken)
    {
        var slug = string.Join('-', attributes.Pairs.Select(pair => Sanitize(pair.Value)));
        var candidate = Sanitize(productCode) + "-" + slug;

        if (taken.Add(candidate))
        {
            return candidate;
        }

        var suffix = 2;
        string numbered;
        do
        {
            numbered = string.Create(CultureInfo.InvariantCulture, $"{candidate}-{suffix}");
            suffix++;
        }
        while (!taken.Add(numbered));

        return numbered;
    }

    private static string Sanitize(string value)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToUpperInvariant(ch));
            }
        }

        return builder.Length == 0 ? "X" : builder.ToString();
    }
}
