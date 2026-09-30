using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// Fast-moving items (task P3-T06 "Do this" #7, SRS §9 RPT-13): top sellers by units and by value.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b> (SRS §9 lists RPT-13 for the owner role) although it carries no cost: it is a thin
/// top-N projection over <see cref="ISalesBreakdownQuery"/> (item dimension), so its net and quantity
/// are that report's - net sales excluding tax, net of returns - and reconcile to it by construction.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IFastMovingReportQuery
{
    /// <summary>The <paramref name="topN"/> best-selling items in <paramref name="range"/>, by units and by value.</summary>
    public Task<FastMovingReport> GetReportAsync(
        ReportDateRange range,
        int topN = 20,
        CancellationToken cancellationToken = default);
}

/// <summary>One item in a fast-moving ranking.</summary>
/// <param name="Rank">1 for the best.</param>
/// <param name="ProductVariantId">The variant; null for the open-item row.</param>
/// <param name="Sku">Its SKU.</param>
/// <param name="Name">The product's name.</param>
/// <param name="QtyBase">Net units sold (sold less returned) in base units.</param>
/// <param name="UomSymbol">The base unit's symbol.</param>
/// <param name="Net">Net sales excluding tax.</param>
public sealed record FastMovingRow(
    int Rank,
    long? ProductVariantId,
    string Sku,
    string Name,
    Quantity QtyBase,
    string? UomSymbol,
    Money Net);

/// <summary>The fast-moving report for a range.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="TopN">How many rows each ranking holds at most.</param>
/// <param name="ByUnits">Ranked by net units sold, most first (ties by net value).</param>
/// <param name="ByValue">Ranked by net sales, largest first.</param>
public sealed record FastMovingReport(
    ReportDateRange Range,
    int TopN,
    IReadOnlyList<FastMovingRow> ByUnits,
    IReadOnlyList<FastMovingRow> ByValue);
