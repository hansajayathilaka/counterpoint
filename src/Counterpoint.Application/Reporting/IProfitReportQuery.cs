using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>What the profit report groups by.</summary>
public enum ProfitGrouping
{
    /// <summary>One row per business date, oldest first.</summary>
    Day,

    /// <summary>One row per calendar month, oldest first.</summary>
    Month,

    /// <summary>One row per category, most profitable first.</summary>
    Category,

    /// <summary>One row per brand, most profitable first.</summary>
    Brand,

    /// <summary>One row per product variant, most profitable first.</summary>
    Item,
}

/// <summary>
/// RPT-03, the profit report: net sales, COGS, gross profit and margin by period, category, brand
/// and item (task P3-T05 "Do this" #3, SRS RPT-03/RPT-07, FR-9.4). Also carries the COGS and margin
/// columns of the item/category/brand sales breakdown (RPT-02).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only, enforced here in the Application layer.</b> <see cref="RequiresRoleAttribute"/>
/// means the only registration the container holds is wrapped with <c>RoleAuthorisation</c>: a
/// cashier session is refused with <see cref="NotAuthorisedException"/> before any query runs, whatever
/// the UI does (CLAUDE.md invariant 8, SRS AC-17).
/// </para>
/// <para>
/// <b>COGS is the snapshot cost, never the current one</b> (CLAUDE.md invariant 10). Period rows use
/// <c>sale.cogs</c> (captured at the time of sale) less the SELLABLE-return equivalent; item,
/// category and brand rows use <c>sale_line.unit_cost x qty_base</c> (the same snapshot) less the
/// same return equivalent. Changing a product's cost afterwards moves none of it.
/// </para>
/// <para>
/// <b>Reconciliation.</b> <see cref="ProfitReport.Totals"/> is the canonical
/// <see cref="ProfitPeriodSummary"/>. Day and month rows add up to it exactly. Item, category and brand
/// rows add up to its net sales exactly and to its COGS to within the sub-0.0001 quantisation of
/// the per-sale <c>sale.cogs</c> header against the unquantised per-line products.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IProfitReportQuery
{
    /// <summary>The profit report for <paramref name="range"/> grouped by <paramref name="grouping"/>.</summary>
    public Task<ProfitReport> GetProfitReportAsync(
        ReportDateRange range,
        ProfitGrouping grouping,
        CancellationToken cancellationToken = default);
}

/// <summary>RPT-03 for a range and grouping.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="Grouping">What the rows group by.</param>
/// <param name="Totals">The canonical period figures including COGS, gross profit and margin.</param>
/// <param name="Rows">The rows - chronological for Day/Month, ranked by gross profit otherwise.</param>
public sealed record ProfitReport(
    ReportDateRange Range,
    ProfitGrouping Grouping,
    ProfitPeriodSummary Totals,
    IReadOnlyList<ProfitRow> Rows);

/// <summary>One row of RPT-03.</summary>
/// <param name="Rank">1 for the most profitable row; null for a chronological (Day/Month) row.</param>
/// <param name="Key">The product variant, category or brand id; null for a period row or the open-item / unfiled row.</param>
/// <param name="Code">The SKU for an item row, empty otherwise.</param>
/// <param name="Name">The date, month (<c>yyyy-MM</c>), product, category or brand name.</param>
/// <param name="QtyBase">Net quantity sold in base units; zero for a period row.</param>
/// <param name="UomSymbol">The base unit's symbol for an item row; null otherwise.</param>
/// <param name="Net">Net sales excluding tax.</param>
/// <param name="Cogs">Cost of goods sold at the snapshot cost, net of sellable returns.</param>
/// <param name="GrossProfit"><see cref="Net"/> minus <see cref="Cogs"/>.</param>
/// <param name="MarginRate"><see cref="GrossProfit"/> over <see cref="Net"/> as a fraction; zero when net is zero.</param>
/// <param name="ShareOfNet">This row's fraction of the rows' total net.</param>
/// <param name="Period">For a Day/Month row, the range it covers (clipped to the report range) - the drill-down key; otherwise null.</param>
public sealed record ProfitRow(
    int? Rank,
    long? Key,
    string Code,
    string Name,
    Quantity QtyBase,
    string? UomSymbol,
    Money Net,
    Money Cogs,
    Money GrossProfit,
    decimal MarginRate,
    decimal ShareOfNet,
    ReportDateRange? Period);
