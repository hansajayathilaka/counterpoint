using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// The returns report: returns by reason, item, disposition, refund method and linked versus
/// unlinked, with the return rate against sales (task P3-T05 "Do this" #4, SRS §9 RPT-14).
/// </summary>
/// <remarks>
/// <b>Owner-only</b>, as SRS §9 lists RPT-14. The rows carry no cost figure; the gate follows the
/// SRS role column, enforced in the Application layer like every other owner report.
/// <para>
/// <b>Value is the pre-tax refund.</b> A row's value is <c>sale_return_line.line_refund</c> - the
/// figure canonical net sales subtracts (<c>sale_return.subtotal</c> is their sum) - so the returns
/// report and the sales reports quote one number for the same return. <see cref="ReturnsReport.TotalRefunded"/>
/// is the separate cash figure (<c>total_refund</c>: tax included, restocking fee netted off).
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IReturnsReportQuery
{
    /// <summary>The returns report for <paramref name="range"/> (by each return's own business date).</summary>
    public Task<ReturnsReport> GetReturnsReportAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default);
}

/// <summary>The returns report for a range.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="ReturnCount">Returns taken.</param>
/// <param name="ReturnsSubtotal">Pre-tax value returned - what net sales subtracts.</param>
/// <param name="TotalRefunded">Cash value refunded (<c>sale_return.total_refund</c>).</param>
/// <param name="SalesBeforeReturns">Canonical net sales plus <see cref="ReturnsSubtotal"/>: net sales had nothing been returned.</param>
/// <param name="BillCount">Completed bills in the range.</param>
/// <param name="ValueReturnRate"><see cref="ReturnsSubtotal"/> over <see cref="SalesBeforeReturns"/>, a fraction; zero when there were no sales.</param>
/// <param name="CountReturnRate"><see cref="ReturnCount"/> over <see cref="BillCount"/>, a fraction; zero when there were no bills.</param>
/// <param name="ByReason">Return lines grouped by reason text, largest value first.</param>
/// <param name="ByItem">Return lines grouped by product variant, largest value first.</param>
/// <param name="ByDisposition">SELLABLE versus DAMAGED.</param>
/// <param name="ByLinkage">Returns against a bill versus unlinked returns.</param>
/// <param name="ByRefundMethod">Returns grouped by how they were refunded.</param>
public sealed record ReturnsReport(
    ReportDateRange Range,
    int ReturnCount,
    Money ReturnsSubtotal,
    Money TotalRefunded,
    Money SalesBeforeReturns,
    int BillCount,
    decimal ValueReturnRate,
    decimal CountReturnRate,
    IReadOnlyList<ReturnsGroupRow> ByReason,
    IReadOnlyList<ReturnsGroupRow> ByItem,
    IReadOnlyList<ReturnsGroupRow> ByDisposition,
    IReadOnlyList<ReturnsGroupRow> ByLinkage,
    IReadOnlyList<ReturnsGroupRow> ByRefundMethod);

/// <summary>One group of a returns breakdown.</summary>
/// <param name="Key">The group's label: the reason text, SKU and name, disposition token, "Linked"/"Unlinked" or refund method.</param>
/// <param name="Count">Return lines in the group (returns, for the linkage and refund-method groups).</param>
/// <param name="QtyBase">Base-unit quantity returned; zero for the linkage and refund-method groups.</param>
/// <param name="Value">Pre-tax value returned.</param>
/// <param name="ShareOfValue">This group's fraction of the breakdown's total value.</param>
public sealed record ReturnsGroupRow(
    string Key,
    int Count,
    Quantity QtyBase,
    Money Value,
    decimal ShareOfValue);
