using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Application.Settings;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// <see cref="ITaxReportQuery"/>: the tax report (task P3-T06 "Do this" #2, SRS RPT-19).
/// </summary>
/// <remarks>
/// <para>
/// <b>Owner-only</b>, registered only wrapped with <c>RoleAuthorisation</c>. No cost column is read.
/// </para>
/// <para>
/// <b>Raw tables only.</b> <c>daily_sales_summary</c> has a single tax total and no per-rate split, so this
/// report is the documented <see cref="ReportSourcePolicy.RawTablesRequired"/> case: it reads <c>sale_line</c>
/// and <c>sale_return_line</c> directly and never the rollups.
/// </para>
/// <para>
/// <b>The rate is the line's own snapshot.</b> Sales are grouped by <c>sale_line.tax_rate</c>; returns take the
/// rate of the sale line they reverse (<c>sale_return_line.sale_line_id</c>). The catalogue's current
/// <c>tax_class</c> is never joined - re-rating a class later must not move a past period. Return lines
/// with no link to a sale line are reported under their own row rather than guessed into a rate. The
/// taxable value and its allocation are <see cref="TaxableLines"/>', shared with the X/Z report.
/// </para>
/// </remarks>
internal sealed class TaxReportQuery : ITaxReportQuery
{
    private const string SalesLinesSql =
        TaxableLines.SelectSql
        + """
         WHERE sa.business_date >= @From AND sa.business_date <= @To
           AND sa.status = 'COMPLETED'
        """
        + TaxableLines.OrderSql;

    // A linked return line takes the rate of the sale line it reverses; an unlinked one groups under NULL.
    // line_refund is pre-tax (sale_return.subtotal is their sum), so it is the returns' taxable value.
    private const string ReturnLinesSql =
        """
        SELECT sl.tax_rate AS TaxRateScaled,
               COALESCE(SUM(srl.line_refund), 0) AS TaxableScaled,
               COALESCE(SUM(srl.tax), 0) AS TaxScaled
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
          LEFT JOIN sale_line sl ON sl.id = srl.sale_line_id
         WHERE sr.business_date >= @From AND sr.business_date <= @To
         GROUP BY sl.tax_rate;
        """;

    private const string SaleHeaderTaxSql =
        """
        SELECT COALESCE(SUM(tax), 0)
          FROM sale
         WHERE business_date >= @From AND business_date <= @To
           AND status = 'COMPLETED';
        """;

    private const string SaleLineTaxSql =
        """
        SELECT COALESCE(SUM(sl.tax), 0)
          FROM sale_line sl
          JOIN sale sa ON sa.id = sl.sale_id
         WHERE sa.business_date >= @From AND sa.business_date <= @To
           AND sa.status = 'COMPLETED';
        """;

    private const string ReturnHeaderTaxSql =
        """
        SELECT COALESCE(SUM(tax), 0)
          FROM sale_return
         WHERE business_date >= @From AND business_date <= @To;
        """;

    private readonly IReportConnectionFactory _connectionFactory;
    private readonly ISettings _settings;

    public TaxReportQuery(IReportConnectionFactory connectionFactory, ISettings settings)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(settings);

        _connectionFactory = connectionFactory;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<TaxReport> GetTaxReportAsync(
        ReportDateRange range,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(range);

        var parameters = new
        {
            From = range.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            To = range.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var saleLines = (await connection.QueryAsync<TaxableLines.TaxLineRow>(
                new CommandDefinition(SalesLinesSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            var returnRows = (await connection.QueryAsync<ReturnRateRow>(
                new CommandDefinition(ReturnLinesSql, parameters, cancellationToken: cancellationToken))
                .ConfigureAwait(false)).ToList();

            var headerTax = await ScalarAsync(connection, SaleHeaderTaxSql, parameters, cancellationToken).ConfigureAwait(false);
            var lineTax = await ScalarAsync(connection, SaleLineTaxSql, parameters, cancellationToken).ConfigureAwait(false);
            var returnHeaderTax = await ScalarAsync(connection, ReturnHeaderTaxSql, parameters, cancellationToken).ConfigureAwait(false);

            var sales = TaxableLines.Allocate(saleLines)
                .GroupBy(line => line.RateScaled)
                .ToDictionary(
                    group => group.Key,
                    group => (Taxable: group.Sum(line => line.TaxableScaled), Tax: group.Sum(line => line.TaxScaled)));

            var returns = returnRows
                .Where(row => row.TaxRateScaled.HasValue)
                .ToDictionary(row => row.TaxRateScaled!.Value, row => (row.TaxableScaled, row.TaxScaled));
            var unlinked = returnRows.FirstOrDefault(row => !row.TaxRateScaled.HasValue);

            var rows = new List<TaxReportRow>();
            foreach (var rate in sales.Keys.Union(returns.Keys).OrderBy(rate => rate))
            {
                var (saleTaxable, saleTax) = sales.GetValueOrDefault(rate);
                var (returnTaxable, returnTax) = returns.GetValueOrDefault(rate);

                rows.Add(ToRow(TaxReportRowKind.Rate, TaxRate.FromScaled(rate), saleTaxable, saleTax, returnTaxable, returnTax));
            }

            if (unlinked is not null && (unlinked.TaxableScaled != 0 || unlinked.TaxScaled != 0))
            {
                rows.Add(ToRow(TaxReportRowKind.UnlinkedReturns, null, 0, 0, unlinked.TaxableScaled, unlinked.TaxScaled));
            }

            var totalSalesTaxable = CanonicalFigures.Sum(rows.Select(row => row.SalesTaxable));
            var totalSalesTax = CanonicalFigures.Sum(rows.Select(row => row.SalesTax));
            var totalReturnsTax = CanonicalFigures.Sum(rows.Select(row => row.ReturnsTax));
            var totalReturnsTaxable = CanonicalFigures.Sum(rows.Select(row => row.ReturnsTaxable));

            var saleHeaderTax = Money.FromScaled(headerTax);
            var saleLineTax = Money.FromScaled(lineTax);
            var returnHeader = Money.FromScaled(returnHeaderTax);

            return new TaxReport(
                range,
                new TaxReportHeader(
                    _settings.Shop.Name,
                    _settings.Shop.TaxRegistrationNumber,
                    _settings.Tax.TaxLabel,
                    _settings.Tax.PricesIncludeTax),
                rows,
                totalSalesTaxable,
                totalSalesTax,
                totalReturnsTaxable,
                totalSalesTaxable - totalReturnsTaxable,
                totalReturnsTax,
                totalSalesTax - totalReturnsTax,
                saleLineTax,
                saleHeaderTax,
                returnHeader,
                totalSalesTax == saleHeaderTax && totalSalesTax == saleLineTax && totalReturnsTax == returnHeader);
        }
    }

    private static TaxReportRow ToRow(
        TaxReportRowKind kind,
        TaxRate? rate,
        long saleTaxable,
        long saleTax,
        long returnTaxable,
        long returnTax) => new(
            kind,
            rate,
            Money.FromScaled(saleTaxable),
            Money.FromScaled(saleTax),
            Money.FromScaled(returnTaxable),
            Money.FromScaled(returnTax),
            Money.FromScaled(saleTaxable) - Money.FromScaled(returnTaxable),
            Money.FromScaled(saleTax) - Money.FromScaled(returnTax));

    private static async Task<long> ScalarAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        object parameters,
        CancellationToken cancellationToken) =>
        await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

    /// <summary>The flat shape Dapper maps a row of <see cref="ReturnLinesSql"/> onto.</summary>
    private sealed class ReturnRateRow
    {
        public long? TaxRateScaled { get; set; }

        public long TaxableScaled { get; set; }

        public long TaxScaled { get; set; }
    }
}
