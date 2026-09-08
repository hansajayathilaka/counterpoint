using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// Generates every combination of a parent product's attribute axes in one operation - size ×
/// length × finish, and the rest of it - skipping any combination that already has a variant
/// (SRS FR-2.6, §2.2's ten-length-by-two-thread-by-three-finish case).
/// </summary>
/// <remarks>
/// A pure function: it returns what <em>would</em> be created, for a screen to show as a preview
/// before anything is written. Committing the preview - creating the rows, in one transaction - is
/// the Application layer's job, not this one.
/// </remarks>
public static class VariantMatrixGenerator
{
    /// <summary>
    /// The cartesian product of <paramref name="axes"/>, minus any combination already present in
    /// <paramref name="existing"/>.
    /// </summary>
    /// <param name="axes">One or more attribute dimensions, each with at least one value.</param>
    /// <param name="existing">The attribute sets of variants the product already has.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="axes"/> is empty, an axis has a blank name, repeats a name already used by
    /// another axis, or has no values.
    /// </exception>
    public static IReadOnlyList<VariantAttributes> Generate(
        IReadOnlyList<VariantAxis> axes,
        IReadOnlyCollection<VariantAttributes> existing)
    {
        ArgumentNullException.ThrowIfNull(axes);
        ArgumentNullException.ThrowIfNull(existing);

        RequireValidAxes(axes);

        var existingSet = new HashSet<VariantAttributes>(existing);

        IEnumerable<Dictionary<string, string>> combinations = [new Dictionary<string, string>(StringComparer.Ordinal)];

        foreach (var axis in axes)
        {
            combinations = combinations.SelectMany(prefix => axis.Values.Select(value =>
            {
                var next = new Dictionary<string, string>(prefix, StringComparer.Ordinal)
                {
                    [axis.Name] = value,
                };
                return next;
            }));
        }

        var result = new List<VariantAttributes>();

        foreach (var combination in combinations)
        {
            var attributes = new VariantAttributes(combination);

            if (existingSet.Add(attributes))
            {
                // Newly added to existingSet: not seen before, so it is a variant to create.
                // Also guards against the same combination appearing twice within this
                // generation, which cannot happen for genuinely distinct axis values but would
                // otherwise silently duplicate a row if it somehow did.
                result.Add(attributes);
            }
        }

        return result;
    }

    private static void RequireValidAxes(IReadOnlyList<VariantAxis> axes)
    {
        if (axes.Count == 0)
        {
            throw new ArgumentException(
                "At least one attribute axis (size, length, finish, ...) is needed to generate variants.",
                nameof(axes));
        }

        var seenNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var axis in axes)
        {
            if (string.IsNullOrWhiteSpace(axis.Name))
            {
                throw new ArgumentException("An axis needs a name.", nameof(axes));
            }

            if (!seenNames.Add(axis.Name))
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Axis '{axis.Name}' is listed more than once."),
                    nameof(axes));
            }

            if (axis.Values is null || axis.Values.Count == 0)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Axis '{axis.Name}' needs at least one value."),
                    nameof(axes));
            }
        }
    }
}
