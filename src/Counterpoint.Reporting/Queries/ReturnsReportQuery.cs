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
/// <see cref="IReturnsReportQuery"/>: the returns report (task P3-T05 "Do this" #4, SRS RPT-14).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b>, registered only wrapped with <c>RoleAuthorisation</c> (see
/// <c>ReportingServiceCollectionExtensions</c>). No cost column is read.
/// </para>
/// <para>
/// <b>One number for a return.</b> Every group's value is the pre-tax refund - line groups sum
/// <c>sale_return_line.line_refund</c>, header groups sum <c>sale_return.subtotal</c> - the figure
/// canonical net sales subtracts. The rate's denominator is the canonical net sales from
/// <see cref="PeriodFiguresReader"/> plus that same returns subtotal (net sales had nothing been
/// returned), so the report never restates "net sales". Returns are dated by their own business date.
/// </para>
/// </remarks>
internal sealed class ReturnsReportQuery : IReturnsReportQuery
{
    private const string ByLinkageSql =
        """
        SELECT CASE WHEN original_sale_id IS NULL THEN 'Unlinked' ELSE 'Linked' END AS GroupKey,
               COUNT(*) AS Count,
               COALESCE(SUM(subtotal), 0) AS ValueScaled,
               COALESCE(SUM(total_refund), 0) AS TotalRefundScaled
          FROM sale_return
         WHERE business_date >= @From AND business_date <= @To
         GROUP BY CASE WHEN original_sale_id IS NULL THEN 'Unlinked' ELSE 'Linked' END;
        """;

    private const string ByRefundMethodSql =
        """
        SELECT refund_method AS GroupKey,
               COUNT(*) AS Count,
               COALESCE(SUM(subtotal), 0) AS ValueScaled,
               COALESCE(SUM(total_refund), 0) AS TotalRefundScaled
          FROM sale_return
         WHERE business_date >= @From AND business_date <= @To
         GROUP BY refund_method;
        """;

    private const string ByReasonSql =
        """
        SELECT COALESCE(NULLIF(TRIM(srl.reason), ''), '(no reason)') AS GroupKey,
               COUNT(*) AS Count,
               COALESCE(SUM(srl.qty_base), 0) AS QtyScaled,
               COALESCE(SUM(srl.line_refund), 0) AS ValueScaled
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
         WHERE sr.business_date >= @From AND sr.business_date <= @To
         GROUP BY COALESCE(NULLIF(TRIM(srl.reason), ''), '(no reason)');
        """;

    private const string ByDispositionSql =
        """
        SELECT srl.disposition AS GroupKey,
               COUNT(*) AS Count,
               COALESCE(SUM(srl.qty_base), 0) AS QtyScaled,
               COALESCE(SUM(srl.line_refund), 0) AS ValueScaled
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
         WHERE sr.business_date >= @From AND sr.business_date <= @To
         GROUP BY srl.disposition;
        """;

    private const string ByItemSql =
        """
        SELECT pv.sku || ' - ' || p.name AS GroupKey,
               COUNT(*) AS Count,
               COALESCE(SUM(srl.qty_base), 0) AS QtyScaled,
               COALESCE(SUM(srl.line_refund), 0) AS ValueScaled
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
          JOIN product_variant pv ON pv.id = srl.product_variant_id
          JOIN product p ON p.id = pv.product_id
         WHERE sr.business_date >= @From AND sr.business_date <= @To
         GROUP BY srl.product_variant_id, pv.sku, p.name;
        """;

    private readonly IReportConnectionFactory _connectionFactory;
    private readonly PeriodFiguresReader _period;

    public ReturnsReportQuery(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);

        _connectionFactory = connectionFactory;
        _period = new PeriodFiguresReader(connectionFactory);
    }

    /// <inheritdoc />
    public async Task<ReturnsReport> GetReturnsReportAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var sales = await _period.ReadTotalsAsync(range, ReportSourcePolicy.RollupsWhereClosed, cancellationToken)
            .ConfigureAwait(false);

        var parameters = new
        {
            From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var linkage = await ReadAsync(connection, ByLinkageSql, parameters, cancellationToken).ConfigureAwait(false);
            var method = await ReadAsync(connection, ByRefundMethodSql, parameters, cancellationToken).ConfigureAwait(false);
            var reason = await ReadAsync(connection, ByReasonSql, parameters, cancellationToken).ConfigureAwait(false);
            var disposition = await ReadAsync(connection, ByDispositionSql, parameters, cancellationToken).ConfigureAwait(false);
            var item = await ReadAsync(connection, ByItemSql, parameters, cancellationToken).ConfigureAwait(false);

            // The linkage split covers every return exactly once, so it is the header total.
            var returnCount = linkage.Sum(row => row.Count);
            var returnsSubtotal = CanonicalFigures.Sum(linkage.Select(row => Money.FromScaled(row.ValueScaled)));
            var totalRefunded = CanonicalFigures.Sum(linkage.Select(row => Money.FromScaled(row.TotalRefundScaled)));

            var salesBeforeReturns = sales.NetSales + returnsSubtotal;

            return new ReturnsReport(
                range,
                returnCount,
                returnsSubtotal,
                totalRefunded,
                salesBeforeReturns,
                sales.BillCount,
                CanonicalFigures.Share(returnsSubtotal, salesBeforeReturns),
                sales.BillCount == 0 ? 0m : (decimal)returnCount / sales.BillCount,
                ToRows(reason),
                ToRows(item),
                ToRows(disposition),
                ToRows(linkage),
                ToRows(method));
        }
    }

    private static async Task<List<GroupRow>> ReadAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        object parameters,
        CancellationToken cancellationToken)
    {
        var rows = await connection.QueryAsync<GroupRow>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows];
    }

    private static IReadOnlyList<ReturnsGroupRow> ToRows(IReadOnlyList<GroupRow> rows)
    {
        var total = CanonicalFigures.Sum(rows.Select(row => Money.FromScaled(row.ValueScaled)));

        return
        [
            .. rows
                .OrderByDescending(row => row.ValueScaled)
                .ThenBy(row => row.GroupKey, StringComparer.Ordinal)
                .Select(row => new ReturnsGroupRow(
                    row.GroupKey,
                    row.Count,
                    Quantity.FromScaled(row.QtyScaled, uomId: 0),
                    Money.FromScaled(row.ValueScaled),
                    CanonicalFigures.Share(Money.FromScaled(row.ValueScaled), total))),
        ];
    }

    /// <summary>The flat shape Dapper maps every group statement onto (header groups carry no quantity).</summary>
    private sealed class GroupRow
    {
        public string GroupKey { get; set; } = string.Empty;

        public int Count { get; set; }

        public long QtyScaled { get; set; }

        public long ValueScaled { get; set; }

        public long TotalRefundScaled { get; set; }
    }
}
