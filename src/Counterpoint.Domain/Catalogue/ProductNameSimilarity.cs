using System;

namespace Counterpoint.Domain.Catalogue;

/// <summary>
/// How close two product names are, for the duplicate-on-creation warning (SRS FR-2.24).
/// </summary>
/// <remarks>
/// Pure text comparison, nothing more: whether two similar-looking products are also the "same
/// brand and size" is decided by the caller, which has the catalogue rows this type never sees.
/// </remarks>
public static class ProductNameSimilarity
{
    /// <summary>
    /// The score at or above which two names are "very similar" for FR-2.24 - close enough that
    /// the difference is plausibly a typo, a re-entry, or a near-duplicate listing, not a
    /// different product that merely shares some words.
    /// </summary>
    /// <remarks>
    /// A <see cref="decimal"/>, not a <c>double</c>: CLAUDE.md invariant 1 bans binary floating
    /// point from this assembly outright, not only for money and quantity, and an architecture
    /// test enforces it. A similarity ratio has no scale to preserve the way a price does; it is
    /// <see cref="decimal"/> here purely to stay on the one arithmetic type this codebase allows.
    /// </remarks>
    public const decimal DefaultThreshold = 0.82m;

    /// <summary>
    /// A similarity score between 0 (nothing in common) and 1 (identical once normalised):
    /// <c>1 - (edit distance / length of the longer normalised name)</c>.
    /// </summary>
    public static decimal Score(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var left = Normalise(a);
        var right = Normalise(b);

        if (left.Length == 0 && right.Length == 0)
        {
            return 1m;
        }

        if (left.Length == 0 || right.Length == 0)
        {
            return 0m;
        }

        var distance = LevenshteinDistance(left, right);
        var longest = Math.Max(left.Length, right.Length);

        return 1m - (decimal)distance / longest;
    }

    /// <summary>True when <see cref="Score"/> reaches <paramref name="threshold"/> (default <see cref="DefaultThreshold"/>).</summary>
    public static bool AreSimilar(string a, string b, decimal threshold = DefaultThreshold) =>
        Score(a, b) >= threshold;

    /// <summary>
    /// Upper-cased, with every run of whitespace collapsed to a single space and leading/trailing
    /// whitespace trimmed - so "  Bosch  Drill " and "BOSCH DRILL" compare as identical.
    /// </summary>
    private static string Normalise(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        Span<char> buffer = trimmed.Length <= 256 ? stackalloc char[trimmed.Length] : new char[trimmed.Length];
        var written = 0;
        var lastWasSpace = false;

        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c))
            {
                if (lastWasSpace)
                {
                    continue;
                }

                buffer[written++] = ' ';
                lastWasSpace = true;
                continue;
            }

            buffer[written++] = char.ToUpperInvariant(c);
            lastWasSpace = false;
        }

        return new string(buffer[..written]);
    }

    /// <summary>Classic full-matrix Levenshtein edit distance. Names are short, so O(n·m) is nothing.</summary>
    private static int LevenshteinDistance(string a, string b)
    {
        var rows = a.Length + 1;
        var cols = b.Length + 1;
        var distance = new int[rows, cols];

        for (var i = 0; i < rows; i++)
        {
            distance[i, 0] = i;
        }

        for (var j = 0; j < cols; j++)
        {
            distance[0, j] = j;
        }

        for (var i = 1; i < rows; i++)
        {
            for (var j = 1; j < cols; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;

                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[a.Length, b.Length];
    }
}
