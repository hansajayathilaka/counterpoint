namespace Counterpoint.Application.Reporting;

/// <summary>
/// The quick date-range presets every report offers (task P3-T04 "Do this" #1, SRS FR-9.1:
/// "today, yesterday, this week, this month, last month, this year, custom").
/// </summary>
/// <remarks>
/// The arithmetic that turns each one into a concrete range lives in exactly one place,
/// <see cref="ReportDateRange.For"/> / <see cref="ReportDateRange.Custom"/>, so a report screen
/// never re-derives "last month" for itself and drifts from every other report by a day.
/// </remarks>
public enum ReportDatePreset
{
    /// <summary>Today only, <c>today .. today</c>.</summary>
    Today,

    /// <summary>Yesterday only, <c>today-1 .. today-1</c>.</summary>
    Yesterday,

    /// <summary>Monday of the current week through today (week starts Monday).</summary>
    ThisWeek,

    /// <summary>The first of the current month through today.</summary>
    ThisMonth,

    /// <summary>The whole of the previous calendar month.</summary>
    LastMonth,

    /// <summary>The first of January of the current year through today.</summary>
    ThisYear,

    /// <summary>An explicit <c>from .. to</c> range supplied by the caller.</summary>
    Custom,
}
