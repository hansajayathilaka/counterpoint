using System;
using System.Globalization;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// A closed business-date range a report runs over, resolved from one of the SRS FR-9.1 presets
/// (task P3-T04 "Do this" #1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Business dates, not timestamps.</b> <see cref="From"/> and <see cref="To"/> are inclusive
/// <see cref="DateOnly"/> values compared against the <c>business_date</c> <c>TEXT</c> column every
/// rollup and every range report groups by (docs/01_DATA_MODEL.md §17: routing a business date
/// through the timestamp converter "would corrupt it"). Both ends are inclusive.
/// </para>
/// <para>
/// <b>Week starts Monday.</b> <see cref="ReportDatePreset.ThisWeek"/> resolves to the Monday of the
/// current week. This is the shop's own convention and is stated in
/// <c>docs/report-definitions.md</c> so it is a written decision rather than an accident of the
/// one implementation.
/// </para>
/// </remarks>
public sealed record ReportDateRange
{
    private ReportDateRange(ReportDatePreset preset, DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The end of a report range ({to:yyyy-MM-dd}) cannot be before its start ({from:yyyy-MM-dd})."),
                nameof(to));
        }

        Preset = preset;
        From = from;
        To = to;
    }

    /// <summary>Which preset produced this range, or <see cref="ReportDatePreset.Custom"/>.</summary>
    public ReportDatePreset Preset { get; }

    /// <summary>The first business date in the range, inclusive.</summary>
    public DateOnly From { get; }

    /// <summary>The last business date in the range, inclusive.</summary>
    public DateOnly To { get; }

    /// <summary>
    /// Resolves one of the fixed presets against a caller-supplied "today". <see cref="ReportDatePreset.Custom"/>
    /// has no fixed meaning and is refused here - use <see cref="Custom"/> for an explicit range.
    /// </summary>
    /// <param name="preset">The preset to resolve. Must not be <see cref="ReportDatePreset.Custom"/>.</param>
    /// <param name="today">The current local business date, injected so the result is testable.</param>
    public static ReportDateRange For(ReportDatePreset preset, DateOnly today)
    {
        return preset switch
        {
            ReportDatePreset.Today => new ReportDateRange(preset, today, today),

            ReportDatePreset.Yesterday => Yesterday(preset, today),

            // Week starts Monday: DayOfWeek.Sunday is 0, so the backward offset to Monday is
            // ((int)dayOfWeek + 6) % 7 - Monday 0, Tuesday 1, ... Sunday 6.
            ReportDatePreset.ThisWeek => new ReportDateRange(
                preset,
                today.AddDays(-(((int)today.DayOfWeek + 6) % 7)),
                today),

            ReportDatePreset.ThisMonth => new ReportDateRange(
                preset,
                new DateOnly(today.Year, today.Month, 1),
                today),

            ReportDatePreset.LastMonth => LastMonth(preset, today),

            ReportDatePreset.ThisYear => new ReportDateRange(preset, new DateOnly(today.Year, 1, 1), today),

            ReportDatePreset.Custom => throw new ArgumentException(
                "A custom range has no fixed meaning - use ReportDateRange.Custom(from, to).",
                nameof(preset)),

            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown report date preset."),
        };
    }

    /// <summary>An explicit, inclusive <c>from .. to</c> range.</summary>
    public static ReportDateRange Custom(DateOnly from, DateOnly to) =>
        new(ReportDatePreset.Custom, from, to);

    private static ReportDateRange Yesterday(ReportDatePreset preset, DateOnly today)
    {
        var yesterday = today.AddDays(-1);
        return new ReportDateRange(preset, yesterday, yesterday);
    }

    private static ReportDateRange LastMonth(ReportDatePreset preset, DateOnly today)
    {
        var firstOfThisMonth = new DateOnly(today.Year, today.Month, 1);
        var firstOfLastMonth = firstOfThisMonth.AddMonths(-1);
        return new ReportDateRange(preset, firstOfLastMonth, firstOfThisMonth.AddDays(-1));
    }
}
