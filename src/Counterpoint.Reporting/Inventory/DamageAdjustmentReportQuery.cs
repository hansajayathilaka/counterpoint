using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Inventory;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.Inventory;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Reporting.Queries;
using Dapper;

namespace Counterpoint.Reporting.Inventory;

/// <summary>
/// <see cref="IDamageAdjustmentReportQuery"/>: damage, write-offs, adjustments and damaged returns (task
/// P3-T06 "Do this" #1, SRS RPT-15).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b>, registered only wrapped with <c>RoleAuthorisation</c>.
/// </para>
/// <para>
/// Ledger adjustment and damage movements are read through <see cref="IAdjustmentHistoryQuery"/> (the
/// owner-only history the exceptions work also consumes) with its calendar-day filter; damaged returns are
/// read here from <c>sale_return_line</c>. Every value is <c>unit cost x quantity</c> multiplied in C# - never
/// scaled-times-scaled in SQL - at the ledger's own movement cost or the return line's cost snapshot, never the
/// catalogue's current cost (CLAUDE.md invariant 10). The loss and gain totals add up individual movements,
/// not the netted rows, so an adjustment that found three and lost five still shows both.
/// </para>
/// </remarks>
internal sealed class DamageAdjustmentReportQuery : IDamageAdjustmentReportQuery
{
    private const string NoReason = "(no reason)";

    private const string DamagedReturnsSql =
        """
        SELECT COALESCE(NULLIF(TRIM(srl.reason), ''), '(no reason)') AS Reason,
               srl.unit_cost AS UnitCostScaled,
               srl.qty_base AS QtyBaseScaled
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
         WHERE srl.disposition = 'DAMAGED'
           AND sr.business_date >= @From AND sr.business_date <= @To;
        """;

    private readonly IReportConnectionFactory _connectionFactory;
    private readonly IAdjustmentHistoryQuery _history;

    public DamageAdjustmentReportQuery(IReportConnectionFactory connectionFactory, IAdjustmentHistoryQuery history)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(history);

        _connectionFactory = connectionFactory;
        _history = history;
    }

    /// <inheritdoc />
    public async Task<DamageAdjustmentReport> GetReportAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var movements = await _history.ListAsync(
            new AdjustmentHistoryFilter(FromDate: range.From, ToDate: range.To), cancellationToken).ConfigureAwait(false);

        List<ReturnRow> damagedReturns;
        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            damagedReturns = (await connection.QueryAsync<ReturnRow>(
                new CommandDefinition(
                    DamagedReturnsSql,
                    new
                    {
                        From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        To = range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    },
                    cancellationToken: cancellationToken)).ConfigureAwait(false)).ToList();
        }

        // One event per ledger movement or damaged return line: (source, reason, qty, value). Quantity is the
        // signed effect on stock; a damaged return is shown as the loss of the goods' cost.
        var events = new List<(DamageSource Source, string Reason, decimal Qty, Money Value)>();

        foreach (var movement in movements)
        {
            var source = string.Equals(movement.MovementType, AdjustmentTypes.ToToken(AdjustmentType.Damage), StringComparison.Ordinal)
                ? DamageSource.Damage
                : DamageSource.Adjustment;

            events.Add((source, ReasonOrDefault(movement.Reason), movement.QtyBase.Value, movement.UnitCost * movement.QtyBase.Value));
        }

        foreach (var line in damagedReturns)
        {
            var qty = Quantity.FromScaled(line.QtyBaseScaled, uomId: 0).Value;
            events.Add((DamageSource.DamagedReturn, line.Reason, -qty, -(Money.FromScaled(line.UnitCostScaled) * qty)));
        }

        var rows = events
            .GroupBy(entry => (entry.Source, entry.Reason))
            .Select(group => new DamageAdjustmentRow(
                group.Key.Source,
                group.Key.Reason,
                group.Count(),
                Quantity.FromDecimal(group.Sum(entry => entry.Qty), uomId: 0),
                CanonicalFigures.Sum(group.Select(entry => entry.Value))))
            .OrderBy(row => row.Value)
            .ThenBy(row => row.Source)
            .ThenBy(row => row.Reason, StringComparer.Ordinal)
            .ToList();

        return new DamageAdjustmentReport(
            range,
            rows,
            CanonicalFigures.Sum(events.Select(entry => entry.Value)),
            -CanonicalFigures.Sum(events.Where(entry => entry.Value < Money.Zero).Select(entry => entry.Value)),
            CanonicalFigures.Sum(events.Where(entry => entry.Value > Money.Zero).Select(entry => entry.Value)));
    }

    private static string ReasonOrDefault(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? NoReason : reason.Trim();

    private sealed class ReturnRow
    {
        public string Reason { get; set; } = string.Empty;

        public long UnitCostScaled { get; set; }

        public long QtyBaseScaled { get; set; }
    }
}
