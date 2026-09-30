using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// Tender reconciliation (task P3-T06 "Do this" #3): tenders by type for a period, tied to the Z
/// reports of the shifts that closed in it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Not SRS §9 RPT-05</b> (that id is the Z report itself); this is the phase plan's "tender / cash
/// reconciliation" and carries no SRS id. It is the AC-12 tie-out as a screen: the sum of the closed
/// shifts' Z tenders against the period's tenders read straight from <c>payment</c>, difference shown
/// and required to be zero.
/// </para>
/// <para>
/// <b>Owner-only</b>, enforced here. No cost figure.
/// </para>
/// <para>
/// <b>The Z side is recomputed, not stored.</b> A Z report is not a table. Each closed shift's tenders
/// are read with the very SQL <c>XReportFiguresReader</c> (and so the Z report that closed the shift)
/// uses, shared in one place so the two cannot drift. <c>daily_sales_summary</c> is never read - it
/// has three tender buckets, this has seven tender types.
/// </para>
/// <para>
/// <b>Nothing is silently omitted; every difference is explained.</b> The two sides disagree on what a
/// "day" is: the Z side takes every payment of each closed shift whose business date (the day it was
/// <i>opened</i>) is in the range, whatever date the bills carry, while the payments side takes payments by
/// the bill's or return's own (wall-clock) date. So a difference has exactly two sources, and each is listed
/// as a <see cref="TenderNotZdItem"/> with a reason: an open shift, or trading dated in the range whose shift
/// is not a closed in-range one ("not Z'd yet", on the payments side only); and a closed in-range shift that
/// also holds trading dated outside the range (on the Z side only, for example a shift opened on the last
/// day of the range that traded past midnight). Each item carries its signed
/// <see cref="TenderNotZdItem.NetEffect"/> on the difference, and every <see cref="TenderTieOutRow.Unexplained"/>
/// is the tender's difference less what the listed items account for - zero unless the ledger really is
/// inconsistent, which is then a defect to report.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface ITenderReconciliationQuery
{
    /// <summary>The reconciliation for <paramref name="range"/>.</summary>
    public Task<TenderReconciliation> GetReconciliationAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default);
}

/// <summary>One closed shift's Z-report tenders.</summary>
/// <param name="ShiftId">The shift.</param>
/// <param name="ShiftNo">Its number.</param>
/// <param name="BusinessDate">The shift's business date.</param>
/// <param name="Tenders">Tenders by type - the same figures the Z report printed (sales, refunds, net).</param>
/// <param name="NetTotal">Sum of every tender's net - what the shift took in net of refunds.</param>
public sealed record TenderShiftRow(
    long ShiftId,
    string ShiftNo,
    DateOnly BusinessDate,
    IReadOnlyList<XReportTenderLine> Tenders,
    Money NetTotal);

/// <summary>One tender type's Z-side total against the range-side total.</summary>
/// <param name="TenderType">A <c>payment.tender_type</c> value.</param>
/// <param name="ZSales">Sum across the closed shifts of tenders taken on sales.</param>
/// <param name="ZRefunds">Sum across the closed shifts of refunds paid out, as a positive magnitude.</param>
/// <param name="ZNet"><see cref="ZSales"/> minus <see cref="ZRefunds"/>.</param>
/// <param name="RangeSales">Tenders on the period's completed sales, straight from <c>payment</c> by the sale's business date.</param>
/// <param name="RangeRefunds">Refunds on the period's returns, by the return's business date, as a positive magnitude.</param>
/// <param name="RangeNet"><see cref="RangeSales"/> minus <see cref="RangeRefunds"/>.</param>
/// <param name="Difference"><see cref="RangeNet"/> minus <see cref="ZNet"/>; zero when the period is fully Z'd and consistent.</param>
/// <param name="Unexplained"><see cref="Difference"/> less the listed <see cref="TenderNotZdItem"/>s' effect on this tender; non-zero only when something is wrong beyond a date boundary or an open shift.</param>
public sealed record TenderTieOutRow(
    string TenderType,
    Money ZSales,
    Money ZRefunds,
    Money ZNet,
    Money RangeSales,
    Money RangeRefunds,
    Money RangeNet,
    Money Difference,
    Money Unexplained);

/// <summary>
/// A shift that accounts for part of a difference: trading in the range that is not (yet) on a closed Z
/// report inside it, or a closed in-range Z report that also holds trading dated outside the range.
/// </summary>
/// <param name="ShiftId">The shift.</param>
/// <param name="ShiftNo">Its number.</param>
/// <param name="Status">"OPEN" or "CLOSED".</param>
/// <param name="BusinessDate">The shift's business date (the day it was opened).</param>
/// <param name="SalesInRange">Completed sales in the range that belong to this shift but are not on a closed in-range Z report.</param>
/// <param name="ReturnsInRange">Returns in the range that belong to this shift but are not on a closed in-range Z report.</param>
/// <param name="Reason">A plain-language reason, for example "Not Z'd yet - the shift is still open."</param>
/// <param name="SalesOutsideRange">Completed sales dated outside the range that are on this closed in-range shift's Z report.</param>
/// <param name="ReturnsOutsideRange">Returns dated outside the range that are on this closed in-range shift's Z report.</param>
/// <param name="NetEffect">This shift's signed effect on the difference (payments side less Z side), all tenders together: plus for trading only the payments side has, minus for trading only the Z side has.</param>
public sealed record TenderNotZdItem(
    long ShiftId,
    string ShiftNo,
    string Status,
    DateOnly BusinessDate,
    int SalesInRange,
    int ReturnsInRange,
    string Reason,
    int SalesOutsideRange,
    int ReturnsOutsideRange,
    Money NetEffect)
{
    /// <summary>True for a closed in-range Z report holding trading dated outside the range (a date boundary, not a missing Z report).</summary>
    public bool IsDateBoundary => SalesOutsideRange + ReturnsOutsideRange > 0;
}

/// <summary>The tender reconciliation for a range.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="Shifts">The closed shifts (by business date) in the range, oldest first.</param>
/// <param name="ByTender">One row per tender type used, Z side against range side.</param>
/// <param name="ZNetTotal">Sum of every Z net.</param>
/// <param name="RangeNetTotal">Sum of every range net - the canonical tender total for the period.</param>
/// <param name="Difference"><see cref="RangeNetTotal"/> minus <see cref="ZNetTotal"/>.</param>
/// <param name="NotZd">Open shifts, trading in the range outside a closed in-range shift, and closed in-range shifts holding trading outside the range; empty when the two sides cover exactly the same trading.</param>
/// <param name="IsTiedOut">True when <see cref="Difference"/> is zero for every tender type and <see cref="NotZd"/> is empty. Never true while an item is listed.</param>
public sealed record TenderReconciliation(
    ReportDateRange Range,
    IReadOnlyList<TenderShiftRow> Shifts,
    IReadOnlyList<TenderTieOutRow> ByTender,
    Money ZNetTotal,
    Money RangeNetTotal,
    Money Difference,
    IReadOnlyList<TenderNotZdItem> NotZd,
    bool IsTiedOut)
{
    /// <summary>True when some tender's difference is not accounted for by the listed <see cref="NotZd"/> items.</summary>
    public bool HasUnexplainedDifference => ByTender.Any(row => !row.Unexplained.IsZero);
}
