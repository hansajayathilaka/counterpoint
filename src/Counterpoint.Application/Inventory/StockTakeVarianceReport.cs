using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// System versus counted, sorted by absolute value impact descending
/// (<see cref="IStockTakeService.BuildVarianceReportAsync"/>, task P2-T10).
/// </summary>
/// <param name="TotalValue">
/// The sum of every line's value impact - null for anything but an owner session, the same
/// stripping <see cref="StockTakeVarianceLine.Value"/> carries.
/// </param>
public sealed record StockTakeVarianceReport(
    long StockTakeId,
    string StockTakeNo,
    string Scope,
    IReadOnlyList<StockTakeVarianceLine> Lines,
    Money? TotalValue);

/// <summary>One line of the variance report.</summary>
/// <param name="Value">
/// <c>Variance × CostAvg</c> at the moment the report was built - owner-only information, null
/// for a cashier session (CLAUDE.md invariant 8), exactly the split
/// <c>StockEnquiryResult.CostAvg</c> already draws. Still used, unstripped, to decide the line's
/// place in <see cref="StockTakeVarianceReport.Lines"/> - only the figure is hidden, never the
/// ordering.
/// </param>
public sealed record StockTakeVarianceLine(
    long ProductVariantId,
    string Sku,
    string ProductName,
    string UomSymbol,
    Quantity SystemQty,
    Quantity? CountedQty,
    Quantity? Variance,
    Money? Value);
