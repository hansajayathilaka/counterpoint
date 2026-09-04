using System;
using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Counterpoint.Infrastructure.Data;

/// <summary>
/// Stores a <see cref="DateTimeOffset"/> as ISO-8601 text with an offset, exactly as
/// docs/01_DATA_MODEL.md §1 requires: <c>2026-09-03T14:22:31.123+05:30</c> (DM-06).
/// </summary>
/// <remarks>
/// <para>
/// Every timestamp column in the schema uses this. EF Core's own SQLite mapping writes
/// <c>yyyy-MM-dd HH:mm:ss.FFFFFFFzzz</c> - a space instead of the <c>T</c>, and a fractional
/// part whose width varies because <c>F</c> trims trailing zeros. Both matter: the column is
/// <c>TEXT</c>, so every <c>ORDER BY</c>, <c>BETWEEN</c> and business-day comparison in the
/// reports is a string comparison, and a variable-width field does not sort.
/// </para>
/// <para>
/// Milliseconds, fixed at three digits. That is the precision the data model documents, and it
/// is far finer than anything a till distinguishes.
/// </para>
/// </remarks>
public sealed class Iso8601TimestampConverter : ValueConverter<DateTimeOffset, string>
{
    /// <summary>
    /// The one format string. Fixed width throughout - no <c>F</c> specifiers, which would trim
    /// trailing zeros and break ordering between two values that differ only in precision.
    /// </summary>
    public const string Format = "yyyy-MM-ddTHH:mm:ss.fffzzz";

    public Iso8601TimestampConverter()
        : base(
            value => value.ToString(Format, CultureInfo.InvariantCulture),
            text => DateTimeOffset.ParseExact(text, Format, CultureInfo.InvariantCulture, DateTimeStyles.None))
    {
    }

    /// <summary>Shared instance; the converter holds no state.</summary>
    public static Iso8601TimestampConverter Instance { get; } = new();
}
