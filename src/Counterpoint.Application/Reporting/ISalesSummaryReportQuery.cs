using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// RPT-01, the sales summary: the canonical period figures for a range, broken down by day, by
/// hour of day and by tender type (task P3-T05 "Do this" #1, SRS §9 RPT-01, FR-9.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not owner-only, and cost-free by construction.</b> Nothing on <see cref="SalesSummaryReport"/>
/// is cost or margin, and the implementation never reads a cost column for it (CLAUDE.md invariant
/// 8, SRS FR-9.4, AC-17).
/// </para>
/// <para>
/// <b>One definition of every figure.</b> <see cref="SalesSummaryReport.Totals"/> is the canonical
/// <see cref="SalesPeriodSummary"/> (<c>docs/report-definitions.md</c> section 2). Every breakdown row is
/// built from the same formulas (<c>CanonicalFigures</c> in <c>Counterpoint.Reporting</c>) over the
/// same raw tables, so the rows of any one breakdown add up to the totals exactly.
/// </para>
/// </remarks>
public interface ISalesSummaryReportQuery
{
    /// <summary>The sales summary for <paramref name="range"/>.</summary>
    /// <param name="range">The inclusive business-date range to report on.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public Task<SalesSummaryReport> GetSummaryAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default);
}

/// <summary>RPT-01: the canonical totals plus the by-day, by-hour and by-tender breakdowns.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="Totals">The canonical period figures for the whole range.</param>
/// <param name="AverageBillValue">
/// Discounted bill value excluding tax and before returns - <c>(gross - discounts) / bill count</c>,
/// or zero with no bills. Returns are separate documents, not bills, so they do not enter it.
/// </param>
/// <param name="ByDay">One row per business date that had any sale or return, oldest first.</param>
/// <param name="ByHour">One row per hour of day (0-23) that had any sale or return, earliest first.</param>
/// <param name="ByTender">One row per tender type used, by name.</param>
public sealed record SalesSummaryReport(
    ReportDateRange Range,
    SalesPeriodSummary Totals,
    Money AverageBillValue,
    IReadOnlyList<SalesDayRow> ByDay,
    IReadOnlyList<SalesHourRow> ByHour,
    IReadOnlyList<SalesTenderRow> ByTender);

/// <summary>One business date of RPT-01. Same definitions as <see cref="SalesPeriodSummary"/>.</summary>
/// <param name="BusinessDate">The business date.</param>
/// <param name="BillCount">Completed bills that day.</param>
/// <param name="ReturnCount">Returns taken that day (dated by the return's own business date).</param>
/// <param name="Gross">Sales before any discount, excluding tax.</param>
/// <param name="Discounts">Line plus bill discounts.</param>
/// <param name="Tax">Tax charged on the day's completed bills.</param>
/// <param name="Net">Gross minus discounts minus returns, excluding tax.</param>
/// <param name="AverageBillValue">See <see cref="SalesSummaryReport.AverageBillValue"/>.</param>
/// <param name="ReturnsValue">The total refunded by the day's returns.</param>
public sealed record SalesDayRow(
    DateOnly BusinessDate,
    int BillCount,
    int ReturnCount,
    Money Gross,
    Money Discounts,
    Money Tax,
    Money Net,
    Money AverageBillValue,
    Money ReturnsValue);

/// <summary>
/// One hour of day of RPT-01 - the wall-clock hour recorded in <c>sold_at</c> (bills) or
/// <c>returned_at</c> (returns), which carries the shop's own offset at the time.
/// </summary>
/// <param name="Hour">The hour, 0-23.</param>
/// <param name="BillCount">Completed bills sold in that hour.</param>
/// <param name="Gross">Sales before any discount, excluding tax.</param>
/// <param name="Discounts">Line plus bill discounts.</param>
/// <param name="Tax">Tax charged on those bills.</param>
/// <param name="Net">Gross minus discounts minus the returns taken in that hour, excluding tax.</param>
/// <param name="AverageBillValue">Discounted bill value excluding tax, per bill.</param>
public sealed record SalesHourRow(
    int Hour,
    int BillCount,
    Money Gross,
    Money Discounts,
    Money Tax,
    Money Net,
    Money AverageBillValue);

/// <summary>One tender type of RPT-01 (SRS §9: "by tender type").</summary>
/// <param name="TenderType">The <c>payment.tender_type</c> token.</param>
/// <param name="SalesAmount">Tendered against completed bills.</param>
/// <param name="RefundsAmount">Paid out against returns, as a positive amount.</param>
/// <param name="NetAmount"><see cref="SalesAmount"/> minus <see cref="RefundsAmount"/>.</param>
public sealed record SalesTenderRow(
    string TenderType,
    Money SalesAmount,
    Money RefundsAmount,
    Money NetAmount);
