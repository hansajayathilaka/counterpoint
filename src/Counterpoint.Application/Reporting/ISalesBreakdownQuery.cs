using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>What a sales breakdown groups by.</summary>
public enum SalesBreakdownDimension
{
    /// <summary>One row per product variant (SKU); open-item lines share one row.</summary>
    Item,

    /// <summary>One row per category, as the product is filed today.</summary>
    Category,

    /// <summary>One row per brand, as the product is filed today.</summary>
    Brand,
}

/// <summary>
/// RPT-02, sales by item, category or brand, ranked by net sales (task P3-T05 "Do this" #2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Not owner-only, and cost-free.</b> The rows carry quantity and net only - no COGS, no
/// margin - and the implementation never selects a cost column for this query (CLAUDE.md invariant
/// 8, SRS AC-17). The COGS and margin columns the task also asks for are
/// <see cref="IProfitReportQuery"/>, owner-only, over the same rows.
/// </para>
/// <para>
/// <b>Net per row is the canonical net, apportioned.</b> A line's net is its <c>line_total</c> less
/// its share of the bill discount (<c>BillDiscountSplit</c>, the same split a return and a receipt
/// use) less the pre-tax refund of any return line for it, so the rows add up to the period's net
/// sales. A bill's category/brand is the product's <i>current</i> filing - the sale line snapshots
/// no category - so re-filing a product moves its history with it.
/// </para>
/// </remarks>
public interface ISalesBreakdownQuery
{
    /// <summary>Sales by <paramref name="dimension"/> for <paramref name="range"/>, best seller first.</summary>
    public Task<SalesBreakdownReport> GetBreakdownAsync(
        ReportDateRange range,
        SalesBreakdownDimension dimension,
        CancellationToken cancellationToken = default);
}

/// <summary>RPT-02 for a range and dimension.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="Dimension">What the rows group by.</param>
/// <param name="TotalNet">The sum of every row's <see cref="SalesBreakdownRow.Net"/>.</param>
/// <param name="Rows">The rows, ranked by net sales descending.</param>
public sealed record SalesBreakdownReport(
    ReportDateRange Range,
    SalesBreakdownDimension Dimension,
    Money TotalNet,
    IReadOnlyList<SalesBreakdownRow> Rows);

/// <summary>One ranked row of RPT-02.</summary>
/// <param name="Rank">1 for the best seller.</param>
/// <param name="Key">The product variant id, category id or brand id; null for the open-item / unfiled row.</param>
/// <param name="Code">The SKU for an item row, empty otherwise.</param>
/// <param name="Name">The product, category or brand name.</param>
/// <param name="QtyBase">Net quantity sold (sold minus returned) in base units; a mix of units for a category or brand row.</param>
/// <param name="UomSymbol">The base unit's symbol for an item row; null for a category or brand row.</param>
/// <param name="Net">Net sales excluding tax.</param>
/// <param name="ShareOfNet">This row's fraction of <see cref="SalesBreakdownReport.TotalNet"/> (0.25 = 25%).</param>
public sealed record SalesBreakdownRow(
    int Rank,
    long? Key,
    string Code,
    string Name,
    Quantity QtyBase,
    string? UomSymbol,
    Money Net,
    decimal ShareOfNet);
