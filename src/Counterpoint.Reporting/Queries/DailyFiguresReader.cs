using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// The canonical figures sliced by business date, by hour of day and by tender type, read from the
/// raw tables (tasks P3-T05: RPT-01 by day/by hour/by tender, RPT-03 by period).
/// </summary>
/// <remarks>
/// <para>
/// <b>Same definitions, sliced.</b> Every figure is computed by <see cref="CanonicalFigures"/> - the
/// same functions <see cref="PeriodFiguresReader"/> uses for the whole period - so the rows of any
/// breakdown sum to the period total exactly (SRS FR-9.6, AC-12). Reading raw rather than rollups is
/// the documented opt-in (<c>ReportSourcePolicy.RawTablesRequired</c>): the rollup has no hour-of-day
/// dimension and only three tender buckets, and a grouped raw read of the indexed
/// <c>business_date</c> column is cheap at this table's size.
/// </para>
/// <para>
/// <b>Cost only on request.</b> <c>includeCost</c> is a query parameter that selects <c>sale.cogs</c>;
/// with it off no cost column is read at all, so a cost-free (cashier-safe) report never computes one.
/// Return COGS is multiplied in C# and only over SELLABLE lines, exactly as the canonical definition
/// states.
/// </para>
/// </remarks>
internal sealed class DailyFiguresReader
{
    private const string SalesByDaySql =
        """
        SELECT business_date AS BusinessDate,
               COUNT(*) AS BillCount,
               COALESCE(SUM(subtotal), 0) AS SubtotalScaled,
               COALESCE(SUM(line_discount), 0) AS LineDiscountScaled,
               COALESCE(SUM(bill_discount), 0) AS BillDiscountScaled,
               COALESCE(SUM(tax), 0) AS TaxScaled,
               COALESCE(SUM(CASE WHEN @IncludeCost = 1 THEN cogs ELSE 0 END), 0) AS CogsScaled
          FROM sale
         WHERE business_date >= @From AND business_date <= @To
           AND status = 'COMPLETED'
         GROUP BY business_date;
        """;

    private const string ReturnsByDaySql =
        """
        SELECT business_date AS BusinessDate,
               COUNT(*) AS ReturnCount,
               COALESCE(SUM(subtotal), 0) AS SubtotalScaled,
               COALESCE(SUM(total_refund), 0) AS TotalRefundScaled
          FROM sale_return
         WHERE business_date >= @From AND business_date <= @To
         GROUP BY business_date;
        """;

    // Only a SELLABLE line recovers its cost (docs/report-definitions.md, COGS).
    private const string ReturnCogsLinesSql =
        """
        SELECT sr.business_date AS BusinessDate, srl.unit_cost AS UnitCostScaled, srl.qty_base AS QtyBaseScaled
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
         WHERE sr.business_date >= @From AND sr.business_date <= @To
           AND srl.disposition = 'SELLABLE';
        """;

    // The hour is the wall-clock hour already recorded in the ISO-8601 timestamp (shop offset at
    // the time), characters 12-13 of "YYYY-MM-DDTHH:mm:ss...".
    private const string SalesByHourSql =
        """
        SELECT CAST(substr(sold_at, 12, 2) AS INTEGER) AS Hour,
               COUNT(*) AS BillCount,
               COALESCE(SUM(subtotal), 0) AS SubtotalScaled,
               COALESCE(SUM(line_discount), 0) AS LineDiscountScaled,
               COALESCE(SUM(bill_discount), 0) AS BillDiscountScaled,
               COALESCE(SUM(tax), 0) AS TaxScaled
          FROM sale
         WHERE business_date >= @From AND business_date <= @To
           AND status = 'COMPLETED'
         GROUP BY CAST(substr(sold_at, 12, 2) AS INTEGER);
        """;

    private const string ReturnsByHourSql =
        """
        SELECT CAST(substr(returned_at, 12, 2) AS INTEGER) AS Hour,
               COALESCE(SUM(subtotal), 0) AS SubtotalScaled
          FROM sale_return
         WHERE business_date >= @From AND business_date <= @To
         GROUP BY CAST(substr(returned_at, 12, 2) AS INTEGER);
        """;

    private const string TendersSql =
        """
        SELECT tender_type AS TenderType,
               COALESCE(SUM(CASE WHEN source = 'SALE' THEN amount ELSE 0 END), 0) AS SalesAmountScaled,
               COALESCE(SUM(CASE WHEN source = 'RETURN' THEN -amount ELSE 0 END), 0) AS RefundsAmountScaled
          FROM (
                SELECT p.tender_type AS tender_type, p.amount AS amount, 'SALE' AS source
                  FROM payment p
                  JOIN sale sa ON sa.id = p.sale_id
                 WHERE sa.business_date >= @From AND sa.business_date <= @To
                   AND sa.status = 'COMPLETED'
                UNION ALL
                SELECT p.tender_type AS tender_type, p.amount AS amount, 'RETURN' AS source
                  FROM payment p
                  JOIN sale_return sr ON sr.id = p.sale_return_id
                 WHERE sr.business_date >= @From AND sr.business_date <= @To
               ) combined
         GROUP BY tender_type
         ORDER BY tender_type;
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    internal DailyFiguresReader(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <summary>One row per business date with any completed sale or any return, oldest first.</summary>
    internal async Task<IReadOnlyList<DayFigures>> ReadDaysAsync(
        ReportDateRange range,
        bool includeCost,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);

        var parameters = new { From = Text(range.From), To = Text(range.To), IncludeCost = includeCost ? 1 : 0 };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var sales = (await connection.QueryAsync<SaleDayRow>(
                new CommandDefinition(SalesByDaySql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false))
                .ToDictionary(row => row.BusinessDate, StringComparer.Ordinal);

            var returns = (await connection.QueryAsync<ReturnDayRow>(
                new CommandDefinition(ReturnsByDaySql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false))
                .ToDictionary(row => row.BusinessDate, StringComparer.Ordinal);

            var returnCogsByDay = new Dictionary<string, Money>(StringComparer.Ordinal);
            if (includeCost)
            {
                var lines = await connection.QueryAsync<ReturnCogsRow>(
                    new CommandDefinition(ReturnCogsLinesSql, parameters, cancellationToken: cancellationToken))
                    .ConfigureAwait(false);

                foreach (var line in lines)
                {
                    var cogs = CanonicalFigures.LineCogs(line.UnitCostScaled, line.QtyBaseScaled);
                    returnCogsByDay[line.BusinessDate] = returnCogsByDay.TryGetValue(line.BusinessDate, out var running)
                        ? running + cogs
                        : cogs;
                }
            }

            var rows = new List<DayFigures>();
            foreach (var date in sales.Keys.Union(returns.Keys, StringComparer.Ordinal).OrderBy(d => d, StringComparer.Ordinal))
            {
                var sale = sales.GetValueOrDefault(date) ?? SaleDayRow.Empty;
                var ret = returns.GetValueOrDefault(date) ?? ReturnDayRow.Empty;

                var gross = CanonicalFigures.Gross(sale.SubtotalScaled, sale.LineDiscountScaled);
                var discounts = CanonicalFigures.Discounts(sale.LineDiscountScaled, sale.BillDiscountScaled);
                var net = CanonicalFigures.Net(
                    sale.SubtotalScaled, sale.LineDiscountScaled, sale.BillDiscountScaled, ret.SubtotalScaled);
                var cogs = Money.FromScaled(sale.CogsScaled) - returnCogsByDay.GetValueOrDefault(date, Money.Zero);

                rows.Add(new DayFigures(
                    DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    sale.BillCount,
                    ret.ReturnCount,
                    gross,
                    discounts,
                    Money.FromScaled(sale.TaxScaled),
                    net,
                    Money.FromScaled(ret.TotalRefundScaled),
                    cogs));
            }

            return rows;
        }
    }

    /// <summary>One row per hour of day (0-23) with any completed sale or any return, earliest first.</summary>
    internal async Task<IReadOnlyList<HourFigures>> ReadHoursAsync(
        ReportDateRange range,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);

        var parameters = new { From = Text(range.From), To = Text(range.To) };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var sales = (await connection.QueryAsync<SaleHourRow>(
                new CommandDefinition(SalesByHourSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false))
                .ToDictionary(row => row.Hour);

            var returns = (await connection.QueryAsync<ReturnHourRow>(
                new CommandDefinition(ReturnsByHourSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false))
                .ToDictionary(row => row.Hour);

            return
            [
                .. sales.Keys.Union(returns.Keys).OrderBy(hour => hour).Select(hour =>
                {
                    var sale = sales.GetValueOrDefault(hour) ?? SaleHourRow.Empty;
                    var ret = returns.GetValueOrDefault(hour) ?? ReturnHourRow.Empty;

                    return new HourFigures(
                        hour,
                        sale.BillCount,
                        CanonicalFigures.Gross(sale.SubtotalScaled, sale.LineDiscountScaled),
                        CanonicalFigures.Discounts(sale.LineDiscountScaled, sale.BillDiscountScaled),
                        Money.FromScaled(sale.TaxScaled),
                        CanonicalFigures.Net(
                            sale.SubtotalScaled, sale.LineDiscountScaled, sale.BillDiscountScaled, ret.SubtotalScaled));
                }),
            ];
        }
    }

    /// <summary>One row per tender type used in the range, by name.</summary>
    internal async Task<IReadOnlyList<TenderFigures>> ReadTendersAsync(
        ReportDateRange range,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);

        var parameters = new { From = Text(range.From), To = Text(range.To) };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var rows = await connection.QueryAsync<TenderRow>(
                new CommandDefinition(TendersSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

            return
            [
                .. rows.Select(row => new TenderFigures(
                    row.TenderType,
                    Money.FromScaled(row.SalesAmountScaled),
                    Money.FromScaled(row.RefundsAmountScaled))),
            ];
        }
    }

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private sealed class SaleDayRow
    {
        internal static readonly SaleDayRow Empty = new();

        public string BusinessDate { get; set; } = string.Empty;

        public int BillCount { get; set; }

        public long SubtotalScaled { get; set; }

        public long LineDiscountScaled { get; set; }

        public long BillDiscountScaled { get; set; }

        public long TaxScaled { get; set; }

        public long CogsScaled { get; set; }
    }

    private sealed class ReturnDayRow
    {
        internal static readonly ReturnDayRow Empty = new();

        public string BusinessDate { get; set; } = string.Empty;

        public int ReturnCount { get; set; }

        public long SubtotalScaled { get; set; }

        public long TotalRefundScaled { get; set; }
    }

    private sealed class ReturnCogsRow
    {
        public string BusinessDate { get; set; } = string.Empty;

        public long UnitCostScaled { get; set; }

        public long QtyBaseScaled { get; set; }
    }

    private sealed class SaleHourRow
    {
        internal static readonly SaleHourRow Empty = new();

        public int Hour { get; set; }

        public int BillCount { get; set; }

        public long SubtotalScaled { get; set; }

        public long LineDiscountScaled { get; set; }

        public long BillDiscountScaled { get; set; }

        public long TaxScaled { get; set; }
    }

    private sealed class ReturnHourRow
    {
        internal static readonly ReturnHourRow Empty = new();

        public int Hour { get; set; }

        public long SubtotalScaled { get; set; }
    }

    private sealed class TenderRow
    {
        public string TenderType { get; set; } = string.Empty;

        public long SalesAmountScaled { get; set; }

        public long RefundsAmountScaled { get; set; }
    }
}

/// <summary>One business date's canonical figures. <see cref="Cogs"/> is zero unless cost was asked for.</summary>
internal sealed record DayFigures(
    DateOnly Date,
    int BillCount,
    int ReturnCount,
    Money Gross,
    Money Discounts,
    Money Tax,
    Money Net,
    Money ReturnsValue,
    Money Cogs);

/// <summary>One hour of day's canonical sales figures.</summary>
internal sealed record HourFigures(int Hour, int BillCount, Money Gross, Money Discounts, Money Tax, Money Net);

/// <summary>One tender type's sales and refunds.</summary>
internal sealed record TenderFigures(string TenderType, Money SalesAmount, Money RefundsAmount);
