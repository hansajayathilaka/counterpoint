using System.Collections.Generic;
using System.Globalization;

namespace Counterpoint.Application.Import;

/// <summary>
/// Turns one <see cref="ImportColumnMapping"/> field value into a column index against a
/// particular file's headers (SRS FR-2.22 "map spreadsheet columns to fields... by header name or
/// position").
/// </summary>
internal static class ColumnIndexResolver
{
    /// <summary>
    /// The 0-based column <paramref name="mapped"/> names in <paramref name="headers"/>, or null
    /// when <paramref name="mapped"/> is blank or names nothing in this file.
    /// </summary>
    /// <remarks>
    /// Header names are matched by exact, case-insensitive text first - the common case, and the
    /// one every <see cref="CatalogueExportColumns"/> round trip relies on. A value that matches no
    /// header falls back to a plain 0-based index, for a file whose first row is not a usable
    /// header at all.
    /// </remarks>
    internal static int? Resolve(IReadOnlyList<string> headers, string? mapped)
    {
        if (string.IsNullOrWhiteSpace(mapped))
        {
            return null;
        }

        var trimmed = mapped.Trim();

        for (var i = 0; i < headers.Count; i++)
        {
            if (string.Equals(headers[i].Trim(), trimmed, System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            && index >= 0
            && index < headers.Count)
        {
            return index;
        }

        return null;
    }

    /// <summary>The trimmed text of column <paramref name="index"/> in <paramref name="row"/>, or null when blank or out of range.</summary>
    internal static string? Cell(IReadOnlyList<string?> row, int? index)
    {
        if (index is not { } i || i >= row.Count)
        {
            return null;
        }

        var text = row[i];
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return text.Trim();
    }
}
