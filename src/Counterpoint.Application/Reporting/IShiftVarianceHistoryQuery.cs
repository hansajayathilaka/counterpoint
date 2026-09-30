using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// The shift and cash-variance history (task P3-T06 "Do this" #4, SRS §9 RPT-21, FR-8.6): over/short
/// per closed shift across a period, with a trend.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b> (SRS §9 lists RPT-21 for the owner role). It reads the <c>shift</c> close
/// fields exactly as the close flow stored them (<c>counted_cash</c>, <c>expected_cash</c>,
/// <c>variance</c>, <c>note</c>, <c>closed_by</c>) and recomputes nothing. A shift is in a period by its
/// own <c>business_date</c>. An open shift has no variance yet and is not listed.
/// </para>
/// <para>
/// <b>Trend</b> is computed in C# from the rows: mean absolute variance of the later half of the
/// shifts against the earlier half (needs at least four shifts). Smaller later is "improving".
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IShiftVarianceHistoryQuery
{
    /// <summary>Closed shifts whose business date is in <paramref name="range"/>, oldest first, with summary and trend.</summary>
    public Task<ShiftVarianceHistory> GetHistoryAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default);
}

/// <summary>Which way cash variance is heading.</summary>
public enum VarianceTrend
{
    /// <summary>Fewer than four closed shifts in the range, so no trend is claimed.</summary>
    NotEnoughData,

    /// <summary>The later half's mean absolute variance is smaller than the earlier half's.</summary>
    Improving,

    /// <summary>Both halves have the same mean absolute variance.</summary>
    Steady,

    /// <summary>The later half's mean absolute variance is larger than the earlier half's.</summary>
    Worsening,
}

/// <summary>One closed shift's cash reconciliation.</summary>
/// <param name="ShiftId">The shift.</param>
/// <param name="ShiftNo">Its number.</param>
/// <param name="BusinessDate">Its business date.</param>
/// <param name="OpenedBy">Who opened it.</param>
/// <param name="ClosedBy">Who closed it (may differ after a recovery, SRS FR-8.7); empty if not recorded.</param>
/// <param name="ClosedAt">When it closed.</param>
/// <param name="CountedCash">The physical count entered at close.</param>
/// <param name="ExpectedCash">What the drawer should have held, frozen at close.</param>
/// <param name="Variance">Counted minus expected; negative is short, positive is over.</param>
/// <param name="Note">The variance note, if one was written.</param>
/// <param name="ExceedsNoteThreshold">True when the absolute variance is above the note threshold in force now.</param>
/// <param name="CumulativeVariance">Running sum of <paramref name="Variance"/> up to and including this shift, oldest first.</param>
public sealed record ShiftVarianceRow(
    long ShiftId,
    string ShiftNo,
    DateOnly BusinessDate,
    string OpenedBy,
    string ClosedBy,
    DateTimeOffset ClosedAt,
    Money CountedCash,
    Money ExpectedCash,
    Money Variance,
    string? Note,
    bool ExceedsNoteThreshold,
    Money CumulativeVariance);

/// <summary>The shift and variance history for a range.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="NoteThreshold">The variance above which the close flow demands a note (<c>policy.shift_close_variance_note_threshold</c>), as configured now.</param>
/// <param name="Rows">Closed shifts, oldest first.</param>
/// <param name="NetVariance">Sum of every variance (over and short offset).</param>
/// <param name="TotalOver">Sum of positive variances.</param>
/// <param name="TotalShort">Sum of negative variances, as a negative number.</param>
/// <param name="MeanAbsoluteVariance">Average of the absolute variances; zero with no shifts.</param>
/// <param name="ShiftsOverThreshold">Shifts whose absolute variance is above <paramref name="NoteThreshold"/>.</param>
/// <param name="Trend">The direction of travel; see <see cref="IShiftVarianceHistoryQuery"/>.</param>
/// <param name="EarlierHalfMeanAbsolute">Mean absolute variance of the earlier half (zero when <paramref name="Trend"/> is <see cref="VarianceTrend.NotEnoughData"/>).</param>
/// <param name="LaterHalfMeanAbsolute">Mean absolute variance of the later half (zero when <paramref name="Trend"/> is <see cref="VarianceTrend.NotEnoughData"/>).</param>
public sealed record ShiftVarianceHistory(
    ReportDateRange Range,
    Money NoteThreshold,
    IReadOnlyList<ShiftVarianceRow> Rows,
    Money NetVariance,
    Money TotalOver,
    Money TotalShort,
    Money MeanAbsoluteVariance,
    int ShiftsOverThreshold,
    VarianceTrend Trend,
    Money EarlierHalfMeanAbsolute,
    Money LaterHalfMeanAbsolute);
