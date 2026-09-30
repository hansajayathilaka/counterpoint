using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Reporting;

/// <summary>
/// Damage and shrinkage (task P3-T06 "Do this" #1, SRS §9 RPT-15): write-offs, damage and
/// adjustments by reason, and damaged returns, with quantity and value.
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b> (SRS §9 lists RPT-15 for the owner role) - every value is cost-derived.
/// </para>
/// <para>
/// <b>Sources.</b> Adjustment and damage movements come from the stock ledger through
/// <see cref="Counterpoint.Application.Inventory.IAdjustmentHistoryQuery"/> (their reason is the
/// movement note). Damaged returns are <c>sale_return_line</c> rows with <c>disposition = 'DAMAGED'</c>:
/// the goods came back and were refunded but are not restocked and post no ledger movement, so they
/// are never double counted with the ledger rows.
/// </para>
/// <para>
/// <b>Value</b> is quantity times the recorded unit cost (the ledger's cost on the movement; the
/// return line's cost snapshot), multiplied in C#. It is the effect on the shop's stock value:
/// negative is a loss. A damaged return is shown as a loss of the cost of the goods.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IDamageAdjustmentReportQuery
{
    /// <summary>Damage, adjustments and damaged returns for <paramref name="range"/>.</summary>
    public Task<DamageAdjustmentReport> GetReportAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default);
}

/// <summary>Where a <see cref="DamageAdjustmentRow"/> came from.</summary>
public enum DamageSource
{
    /// <summary>A stock adjustment (count correction, found or lost stock).</summary>
    Adjustment,

    /// <summary>A damage write-off movement (including bulk-break wastage).</summary>
    Damage,

    /// <summary>A return line received back damaged (not restocked).</summary>
    DamagedReturn,
}

/// <summary>One reason's line of the damage and adjustment summary.</summary>
/// <param name="Source">Adjustment, damage write-off or damaged return.</param>
/// <param name="Reason">The reason text; "(no reason)" when none was recorded.</param>
/// <param name="Count">Movements or return lines in the group.</param>
/// <param name="QtyBase">Net quantity effect on stock in base units (negative is a loss).</param>
/// <param name="Value">Net value effect at recorded cost (negative is a loss).</param>
public sealed record DamageAdjustmentRow(
    DamageSource Source,
    string Reason,
    int Count,
    Quantity QtyBase,
    Money Value);

/// <summary>The damage and adjustment summary for a range.</summary>
/// <param name="Range">The range covered.</param>
/// <param name="Rows">Groups, largest loss first.</param>
/// <param name="NetValue">Sum of every row's value.</param>
/// <param name="TotalLoss">Sum of the negative row values, as a positive magnitude - what was lost.</param>
/// <param name="TotalGain">Sum of the positive row values (for example stock found on a count).</param>
public sealed record DamageAdjustmentReport(
    ReportDateRange Range,
    IReadOnlyList<DamageAdjustmentRow> Rows,
    Money NetValue,
    Money TotalLoss,
    Money TotalGain);
