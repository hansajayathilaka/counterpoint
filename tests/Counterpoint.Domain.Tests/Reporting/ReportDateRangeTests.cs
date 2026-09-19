using System;
using Counterpoint.Application.Reporting;
using FluentAssertions;

namespace Counterpoint.Domain.Tests.Reporting;

/// <summary>
/// The FR-9.1 date-range presets (task P3-T04 "Do this" #1): one implementation, resolved here so
/// no two reports can disagree about what "last month" means.
/// </summary>
/// <remarks>
/// A preset range keeps its own <see cref="ReportDateRange.Preset"/>, so a named preset is never
/// equal to an equivalent <see cref="ReportDateRange.Custom"/> range - that is deliberate, because
/// the preset is what a report prints as its own heading ("Last month", not "1 Aug - 31 Aug"). The
/// assertions below therefore compare <see cref="ReportDateRange.From"/> and
/// <see cref="ReportDateRange.To"/>, and check the preset separately.
/// </remarks>
public sealed class ReportDateRangeTests
{
    [Fact]
    public void TodayAndYesterdayResolveToTheSingleDay()
    {
        var today = new DateOnly(2026, 9, 9);

        var current = ReportDateRange.For(ReportDatePreset.Today, today);
        current.Preset.Should().Be(ReportDatePreset.Today);
        current.From.Should().Be(today);
        current.To.Should().Be(today);

        var previous = ReportDateRange.For(ReportDatePreset.Yesterday, today);
        previous.Preset.Should().Be(ReportDatePreset.Yesterday);
        previous.From.Should().Be(new DateOnly(2026, 9, 8));
        previous.To.Should().Be(new DateOnly(2026, 9, 8));
    }

    [Fact]
    public void ThisWeekStartsOnMonday()
    {
        // 2026-09-09 is a Wednesday, so the week runs from Monday 2026-09-07.
        var wednesday = new DateOnly(2026, 9, 9);
        wednesday.DayOfWeek.Should().Be(DayOfWeek.Wednesday, "the assertion below depends on it");

        var midWeek = ReportDateRange.For(ReportDatePreset.ThisWeek, wednesday);
        midWeek.From.Should().Be(new DateOnly(2026, 9, 7));
        midWeek.To.Should().Be(wednesday, "this week runs to date, not to the coming Sunday");

        // Sunday is the last day of the week, so its "this week" still starts on Monday six days back.
        var sunday = new DateOnly(2026, 9, 13);
        sunday.DayOfWeek.Should().Be(DayOfWeek.Sunday);

        var endOfWeek = ReportDateRange.For(ReportDatePreset.ThisWeek, sunday);
        endOfWeek.From.Should().Be(new DateOnly(2026, 9, 7));
        endOfWeek.To.Should().Be(sunday);

        // Monday itself is already the first day of its own week.
        var monday = new DateOnly(2026, 9, 7);
        monday.DayOfWeek.Should().Be(DayOfWeek.Monday);

        var startOfWeek = ReportDateRange.For(ReportDatePreset.ThisWeek, monday);
        startOfWeek.From.Should().Be(monday);
        startOfWeek.To.Should().Be(monday);
    }

    [Fact]
    public void ThisMonthRunsFromTheFirstToToday()
    {
        var range = ReportDateRange.For(ReportDatePreset.ThisMonth, new DateOnly(2026, 9, 9));

        range.Preset.Should().Be(ReportDatePreset.ThisMonth);
        range.From.Should().Be(new DateOnly(2026, 9, 1));
        range.To.Should().Be(new DateOnly(2026, 9, 9));
    }

    [Fact]
    public void LastMonthIsTheWholePreviousCalendarMonth()
    {
        var august = ReportDateRange.For(ReportDatePreset.LastMonth, new DateOnly(2026, 9, 9));
        august.Preset.Should().Be(ReportDatePreset.LastMonth);
        august.From.Should().Be(new DateOnly(2026, 8, 1));
        august.To.Should().Be(new DateOnly(2026, 8, 31));

        // Across a year boundary, and a February that ends on the 28th in a non-leap year.
        var december = ReportDateRange.For(ReportDatePreset.LastMonth, new DateOnly(2026, 1, 15));
        december.From.Should().Be(new DateOnly(2025, 12, 1));
        december.To.Should().Be(new DateOnly(2025, 12, 31));

        var february = ReportDateRange.For(ReportDatePreset.LastMonth, new DateOnly(2026, 3, 31));
        february.From.Should().Be(new DateOnly(2026, 2, 1));
        february.To.Should().Be(new DateOnly(2026, 2, 28));
    }

    [Fact]
    public void ThisYearRunsFromJanuaryFirstToToday()
    {
        var range = ReportDateRange.For(ReportDatePreset.ThisYear, new DateOnly(2026, 9, 9));

        range.Preset.Should().Be(ReportDatePreset.ThisYear);
        range.From.Should().Be(new DateOnly(2026, 1, 1));
        range.To.Should().Be(new DateOnly(2026, 9, 9));
    }

    [Fact]
    public void CustomKeepsItsExplicitEndsAndRefusesAnInvertedRange()
    {
        var range = ReportDateRange.Custom(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        range.Preset.Should().Be(ReportDatePreset.Custom);
        range.From.Should().Be(new DateOnly(2026, 9, 1));
        range.To.Should().Be(new DateOnly(2026, 9, 30));

        var inverted = () => ReportDateRange.Custom(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 1));
        inverted.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ForRefusesTheCustomPresetBecauseItHasNoFixedMeaning()
    {
        var act = () => ReportDateRange.For(ReportDatePreset.Custom, new DateOnly(2026, 9, 9));

        act.Should().Throw<ArgumentException>();
    }
}
