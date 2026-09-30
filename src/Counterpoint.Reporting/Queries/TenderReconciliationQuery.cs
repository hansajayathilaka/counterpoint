using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Reporting.Shifts;
using Dapper;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="ITenderReconciliationQuery"/>: tender reconciliation (task P3-T06 "Do this" #3; not SRS
/// RPT-05, which is the Z report).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b>, registered only wrapped with <c>RoleAuthorisation</c>. No cost column is read.
/// </para>
/// <para>
/// <b>Two independent reads that must agree.</b> The Z side runs <see cref="ShiftTenderBreakdown.Sql"/> - the
/// SQL behind the X and Z reports - once per closed shift whose <c>business_date</c> is in the range. The
/// range side is <see cref="DailyFiguresReader.ReadTendersAsync"/>, the same definition the sales summary's
/// by-tender table uses, read from <c>payment</c> joined to its sale or return by business date.
/// <c>daily_sales_summary</c> is deliberately not read: its three tender buckets cannot name the seven types.
/// </para>
/// <para>
/// <b>Why the sides can differ, and listing every reason.</b> The Z side is per shift (every payment of a
/// closed shift whose opening business date is in the range, whatever date the bills carry); the range side
/// is per document date. So (1) an open shift in the range, and any sale or return dated in the range whose
/// shift is not a closed in-range one, is on the range side only; (2) a closed in-range shift that also holds
/// sales or returns dated outside the range (opened on the range's last day, traded past midnight) is on the
/// Z side only. Both are listed with a reason and their signed effect on the difference, so every non-zero
/// difference is accounted for; <see cref="TenderTieOutRow.Unexplained"/> is what is left, and is non-zero
/// only for a genuine inconsistency.
/// </para>
/// </remarks>
internal sealed class TenderReconciliationQuery : ITenderReconciliationQuery
{
    private const string ClosedShiftsSql =
        """
        SELECT id AS ShiftId, shift_no AS ShiftNo, business_date AS BusinessDate
          FROM shift
         WHERE status = 'CLOSED'
           AND business_date >= @From AND business_date <= @To
         ORDER BY business_date, id;
        """;

    // Documents are found through their own date indexes first, then joined to their shift; a shift is
    // "not Z'd" when it is not a closed shift dated inside the range, yet holds trading in the range - or
    // is an open shift dated inside it.
    private const string NotZdSql =
        """
        WITH in_range AS (
            SELECT shift_id AS ShiftId, COUNT(*) AS SalesCount, 0 AS ReturnCount
              FROM sale
             WHERE status = 'COMPLETED'
               AND business_date >= @From AND business_date <= @To
             GROUP BY shift_id
            UNION ALL
            SELECT shift_id AS ShiftId, 0 AS SalesCount, COUNT(*) AS ReturnCount
              FROM sale_return
             WHERE business_date >= @From AND business_date <= @To
             GROUP BY shift_id
        ),
        docs AS (
            SELECT ShiftId, SUM(SalesCount) AS SalesCount, SUM(ReturnCount) AS ReturnCount
              FROM in_range
             GROUP BY ShiftId
        )
        SELECT sh.id AS ShiftId,
               sh.shift_no AS ShiftNo,
               sh.status AS Status,
               sh.business_date AS BusinessDate,
               COALESCE(d.SalesCount, 0) AS SalesInRange,
               COALESCE(d.ReturnCount, 0) AS ReturnsInRange
          FROM shift sh
          LEFT JOIN docs d ON d.ShiftId = sh.id
         WHERE NOT (sh.status = 'CLOSED' AND sh.business_date >= @From AND sh.business_date <= @To)
           AND (d.ShiftId IS NOT NULL
                OR (sh.status = 'OPEN' AND sh.business_date >= @From AND sh.business_date <= @To))
         ORDER BY sh.business_date, sh.id;
        """;

    // Closed in-range shifts' completed sales and returns dated OUTSIDE the range: on the Z side, not on the
    // range side.
    private const string OutsideRangeSql =
        """
        WITH closed AS (
            SELECT id, shift_no, business_date
              FROM shift
             WHERE status = 'CLOSED'
               AND business_date >= @From AND business_date <= @To
        ),
        docs AS (
            SELECT shift_id AS ShiftId, business_date AS DocDate, 1 AS IsSale
              FROM sale
             WHERE status = 'COMPLETED'
               AND shift_id IN (SELECT id FROM closed)
               AND (business_date < @From OR business_date > @To)
            UNION ALL
            SELECT shift_id AS ShiftId, business_date AS DocDate, 0 AS IsSale
              FROM sale_return
             WHERE shift_id IN (SELECT id FROM closed)
               AND (business_date < @From OR business_date > @To)
        )
        SELECT c.id AS ShiftId,
               c.shift_no AS ShiftNo,
               c.business_date AS BusinessDate,
               SUM(d.IsSale) AS SalesOutsideRange,
               COUNT(*) - SUM(d.IsSale) AS ReturnsOutsideRange,
               MIN(d.DocDate) AS FirstOutside,
               MAX(d.DocDate) AS LastOutside
          FROM docs d
          JOIN closed c ON c.id = d.ShiftId
         GROUP BY c.id, c.shift_no, c.business_date
         ORDER BY c.business_date, c.id;
        """;

    // Each shift's signed effect on (range side - Z side), by tender, from the payments themselves. A closed
    // in-range shift is on the Z side whatever its documents' dates, so the payments dated outside the range
    // are Z-only (negative); any other shift is on the range side only, for the payments dated inside it.
    // A payment's amount is already signed: a refund is negative, so the net needs no further negation.
    private const string EffectSql =
        """
        WITH closed AS (
            SELECT id
              FROM shift
             WHERE status = 'CLOSED'
               AND business_date >= @From AND business_date <= @To
        ),
        pay AS (
            SELECT sa.shift_id AS ShiftId, sa.business_date AS DocDate, p.tender_type AS TenderType, p.amount AS Amount
              FROM payment p
              JOIN sale sa ON sa.id = p.sale_id
             WHERE sa.status = 'COMPLETED'
               AND ((sa.business_date >= @From AND sa.business_date <= @To) OR sa.shift_id IN (SELECT id FROM closed))
            UNION ALL
            SELECT sr.shift_id, sr.business_date, p.tender_type, p.amount
              FROM payment p
              JOIN sale_return sr ON sr.id = p.sale_return_id
             WHERE ((sr.business_date >= @From AND sr.business_date <= @To) OR sr.shift_id IN (SELECT id FROM closed))
        )
        SELECT ShiftId,
               TenderType,
               SUM(CASE
                       WHEN ShiftId IN (SELECT id FROM closed)
                           THEN CASE WHEN DocDate >= @From AND DocDate <= @To THEN 0 ELSE -Amount END
                       ELSE CASE WHEN DocDate >= @From AND DocDate <= @To THEN Amount ELSE 0 END
                   END) AS EffectScaled
          FROM pay
         GROUP BY ShiftId, TenderType
        HAVING EffectScaled <> 0;
        """;

    private readonly IReportConnectionFactory _connectionFactory;
    private readonly DailyFiguresReader _daily;

    public TenderReconciliationQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _connectionFactory = connectionFactory;
        _daily = new DailyFiguresReader(connectionFactory);
    }

    /// <inheritdoc />
    public async Task<TenderReconciliation> GetReconciliationAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var parameters = new
        {
            From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        var shifts = new List<TenderShiftRow>();
        List<NotZdRow> notZdRows;
        List<OutsideRangeRow> outsideRows;
        List<EffectRow> effectRows;

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var closed = (await connection.QueryAsync<ClosedShiftRow>(
                new CommandDefinition(ClosedShiftsSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            foreach (var shift in closed)
            {
                var tenderRows = await connection.QueryAsync<ShiftTenderBreakdown.TenderRow>(
                    new CommandDefinition(
                        ShiftTenderBreakdown.Sql,
                        new { shift.ShiftId },
                        cancellationToken: cancellationToken)).ConfigureAwait(false);

                var tenders = tenderRows.Select(ShiftTenderBreakdown.ToTenderLine).ToList();

                shifts.Add(new TenderShiftRow(
                    shift.ShiftId,
                    shift.ShiftNo,
                    ParseDate(shift.BusinessDate),
                    tenders,
                    CanonicalFigures.Sum(tenders.Select(tender => tender.NetAmount))));
            }

            notZdRows = (await connection.QueryAsync<NotZdRow>(
                new CommandDefinition(NotZdSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            outsideRows = (await connection.QueryAsync<OutsideRangeRow>(
                new CommandDefinition(OutsideRangeSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            effectRows = (await connection.QueryAsync<EffectRow>(
                new CommandDefinition(EffectSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();
        }

        var rangeSide = await _daily.ReadTendersAsync(range, cancellationToken).ConfigureAwait(false);

        var byTender = new List<TenderTieOutRow>();
        var types = shifts.SelectMany(shift => shift.Tenders.Select(tender => tender.TenderType))
            .Union(rangeSide.Select(tender => tender.TenderType), StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(type => type, StringComparer.Ordinal);

        foreach (var type in types)
        {
            var zLines = shifts.SelectMany(shift => shift.Tenders).Where(tender => tender.TenderType == type).ToList();
            var zSales = CanonicalFigures.Sum(zLines.Select(line => line.SalesAmount));
            var zRefunds = CanonicalFigures.Sum(zLines.Select(line => line.RefundsAmount));

            var range1 = rangeSide.FirstOrDefault(tender => tender.TenderType == type);
            var rangeSales = range1?.SalesAmount ?? Money.Zero;
            var rangeRefunds = range1?.RefundsAmount ?? Money.Zero;

            var zNet = zSales - zRefunds;
            var rangeNet = rangeSales - rangeRefunds;

            var difference = rangeNet - zNet;
            var explained = CanonicalFigures.Sum(
                effectRows.Where(effect => effect.TenderType == type).Select(effect => Money.FromScaled(effect.EffectScaled)));

            byTender.Add(new TenderTieOutRow(
                type, zSales, zRefunds, zNet, rangeSales, rangeRefunds, rangeNet, difference, difference - explained));
        }

        var effectByShift = effectRows
            .GroupBy(effect => effect.ShiftId)
            .ToDictionary(
                group => group.Key,
                group => CanonicalFigures.Sum(group.Select(effect => Money.FromScaled(effect.EffectScaled))));

        var notZd = notZdRows
            .Select(row => ToNotZd(row, effectByShift.GetValueOrDefault(row.ShiftId)))
            .Concat(outsideRows.Select(row => ToOutsideRange(row, effectByShift.GetValueOrDefault(row.ShiftId))))
            .OrderBy(item => item.BusinessDate)
            .ThenBy(item => item.ShiftId)
            .ToList();

        var zNetTotal = CanonicalFigures.Sum(byTender.Select(row => row.ZNet));
        var rangeNetTotal = CanonicalFigures.Sum(byTender.Select(row => row.RangeNet));

        return new TenderReconciliation(
            range,
            shifts,
            byTender,
            zNetTotal,
            rangeNetTotal,
            rangeNetTotal - zNetTotal,
            notZd,
            notZd.Count == 0 && byTender.All(row => row.Difference == Money.Zero));
    }

    private static TenderNotZdItem ToNotZd(NotZdRow row, Money netEffect)
    {
        var date = ParseDate(row.BusinessDate);
        var open = string.Equals(row.Status, "OPEN", StringComparison.Ordinal);

        return new TenderNotZdItem(
            row.ShiftId,
            row.ShiftNo,
            row.Status,
            date,
            row.SalesInRange,
            row.ReturnsInRange,
            open
                ? "Not Z'd yet - this shift is still open."
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"This shift's Z report is dated {date:yyyy-MM-dd}, outside this range, but it holds trading inside it."),
            0,
            0,
            netEffect);
    }

    private static TenderNotZdItem ToOutsideRange(OutsideRangeRow row, Money netEffect)
    {
        var date = ParseDate(row.BusinessDate);
        var first = ParseDate(row.FirstOutside);
        var last = ParseDate(row.LastOutside);
        var outside = first == last
            ? string.Create(CultureInfo.InvariantCulture, $"{first:yyyy-MM-dd}")
            : string.Create(CultureInfo.InvariantCulture, $"{first:yyyy-MM-dd} to {last:yyyy-MM-dd}");

        return new TenderNotZdItem(
            row.ShiftId,
            row.ShiftNo,
            "CLOSED",
            date,
            0,
            0,
            string.Create(
                CultureInfo.InvariantCulture,
                $"This shift's Z report is dated {date:yyyy-MM-dd} but it also holds trading dated {outside}, outside this range."),
            row.SalesOutsideRange,
            row.ReturnsOutsideRange,
            netEffect);
    }

    private static DateOnly ParseDate(string text) =>
        DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class ClosedShiftRow
    {
        public long ShiftId { get; set; }

        public string ShiftNo { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;
    }

    private sealed class NotZdRow
    {
        public long ShiftId { get; set; }

        public string ShiftNo { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public int SalesInRange { get; set; }

        public int ReturnsInRange { get; set; }
    }

    private sealed class OutsideRangeRow
    {
        public long ShiftId { get; set; }

        public string ShiftNo { get; set; } = string.Empty;

        public string BusinessDate { get; set; } = string.Empty;

        public int SalesOutsideRange { get; set; }

        public int ReturnsOutsideRange { get; set; }

        public string FirstOutside { get; set; } = string.Empty;

        public string LastOutside { get; set; } = string.Empty;
    }

    private sealed class EffectRow
    {
        public long ShiftId { get; set; }

        public string TenderType { get; set; } = string.Empty;

        public long EffectScaled { get; set; }
    }
}
