using System;
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Application.Reporting;
using Counterpoint.Domain.ValueObjects;
using Dapper;

namespace Counterpoint.Reporting.Queries;

/// <summary>
/// The one implementation of each canonical report definition (gross sales, net sales, COGS, gross
/// profit, tender total) for a business-date range (task P3-T04 "Do this" #2, SRS FR-9.1, FR-9.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>One implementation, written down.</b> Every formula here is defined in
/// <c>docs/report-definitions.md</c> and implemented nowhere else in the report layer. The two
/// public queries (<c>SalesPeriodSummaryQuery</c>, <c>ProfitPeriodSummaryQuery</c>) and every report
/// screen P3-T05/P3-T06 build on top of them project these figures rather than recomputing them, so
/// there is one definition of "net sales" rather than five (task P3-T04's own context, SRS AC-12).
/// </para>
/// <para>
/// <b>The raw formulas mirror <c>DailyRollupCalculator</c> exactly, on purpose.</b> That class
/// (P3-T03, in <c>Counterpoint.Infrastructure</c>) is what writes <c>daily_sales_summary</c>. The
/// report layer may not reference it (CLAUDE.md "Project boundaries": <c>Counterpoint.Reporting</c>
/// sees only <c>Application</c> and <c>Domain</c>), so the same formulas are written a second time
/// here - and task P3-T04's own "Done when" proves the two agree by asserting that a range spanning
/// closed and open periods (rollups + raw) returns exactly what the same range computed from raw
/// tables alone returns. In particular:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <b>Gross is <c>subtotal + line_discount</c></b>, never <c>subtotal</c> alone - <c>sale.subtotal</c>
/// is already net of the line discount.
/// </description></item>
/// <item><description>
/// <b>Net is gross − discounts − returns</b>, which reduces to
/// <c>subtotal − bill_discount − return_subtotal</c>.
/// </description></item>
/// <item><description>
/// <b>COGS is the <c>sale.cogs</c> snapshot minus the returned equivalent</b>, and the returned
/// equivalent is computed in C# through <see cref="Money"/> - never <c>unit_cost * qty_base</c> in
/// SQL, which would multiply two scaled integers and double-scale the result.
/// </description></item>
/// </list>
/// <para>
/// <b>Routing.</b> The rollup segment is bounded by the caller's own range on both ends
/// (<c>business_date &gt;= @From AND business_date &lt;= @To</c>) and by the split key on the upper
/// side; the split key is the open shift's <c>business_date</c>. Rollups are read for business dates
/// strictly before it; raw tables for dates on or after it - so a date that holds a closed morning
/// shift and an open afternoon shift is read from raw in full, never partly from its own (stale)
/// rollup row. A closed date whose rollup row is absent is also read from raw, so the figures
/// reconcile with raw data on every database rather than silently reading zero for a date no Z report
/// has rolled up yet. A date whose rollup row exists but may be stale - it holds a sale that is not
/// <c>COMPLETED</c>, so a cancellation landed after the rollup was built - is read from raw too. The
/// two segments thus share one three-part predicate, and every business date is read from exactly one
/// source.
/// </para>
/// </remarks>
internal sealed class PeriodFiguresReader
{
    private const string OpenShiftDateSql =
        "SELECT business_date FROM shift WHERE status = 'OPEN' LIMIT 1;";

    // ---- Rollup segment: business dates that are closed, carry a rollup row, and are trustworthy

    // The two rollup statements carry the same three predicates and must be kept in step:
    //   business_date >= @From AND business_date <= @To  - the caller's range, inclusive at both
    //     ends. The upper bound is @To, not @SplitKey: the split key is the open shift's own
    //     business date, which can sit *past* the range ("last month" while a shift is open today),
    //     and the rollup segment must never spill a day the caller did not ask for.
    //   business_date < @SplitKey                          - a date on or after the open shift's is
    //     read from raw in full (it may hold a closed morning and a still-open afternoon shift).
    //   NOT EXISTS (a sale that is not COMPLETED)          - distrust the rollup for a date whose
    //     rollup may be stale. A completed bill can be cancelled after its shift has closed, and
    //     nothing rebuilds daily_sales_summary then, so that date's row would keep counting the
    //     cancelled bill while the raw tables do not. Reading the date from raw instead is correct
    //     either way: if the cancellation predates the rollup, DailyRollupCalculator already
    //     filtered status = 'COMPLETED' and raw returns the same answer; if it postdates it, raw is
    //     the only correct source.
    private const string RollupTotalsSql =
        """
        SELECT COALESCE(SUM(bill_count), 0) AS BillCount,
               COALESCE(SUM(gross), 0) AS GrossScaled,
               COALESCE(SUM(discount), 0) AS DiscountScaled,
               COALESCE(SUM(tax), 0) AS TaxScaled,
               COALESCE(SUM(net), 0) AS NetScaled,
               COALESCE(SUM(return_count), 0) AS ReturnCount,
               COALESCE(SUM(return_value), 0) AS ReturnsValueScaled,
               COALESCE(SUM(tender_cash + tender_card + tender_other), 0) AS TenderTotalScaled
          FROM daily_sales_summary
         WHERE business_date >= @From AND business_date <= @To AND business_date < @SplitKey
           AND NOT EXISTS (
                 SELECT 1 FROM sale stale
                  WHERE stale.business_date = daily_sales_summary.business_date
                    AND stale.status <> 'COMPLETED');
        """;

    private const string RollupCogsSql =
        """
        SELECT COALESCE(SUM(cogs), 0)
          FROM daily_sales_summary
         WHERE business_date >= @From AND business_date <= @To AND business_date < @SplitKey
           AND NOT EXISTS (
                 SELECT 1 FROM sale stale
                  WHERE stale.business_date = daily_sales_summary.business_date
                    AND stale.status <> 'COMPLETED');
        """;

    // ---- Raw segment: the open shift's own date, any closed date with no rollup row, and any
    //      date whose rollup the rollup segment above distrusted - the three must stay in step ----

    private const string RawSalesSql =
        """
        SELECT COUNT(*) AS BillCount,
               COALESCE(SUM(sa.subtotal), 0) AS SubtotalScaled,
               COALESCE(SUM(sa.line_discount), 0) AS LineDiscountScaled,
               COALESCE(SUM(sa.bill_discount), 0) AS BillDiscountScaled,
               COALESCE(SUM(sa.tax), 0) AS TaxScaled,
               COALESCE(SUM(sa.cogs), 0) AS CogsScaled
          FROM sale sa
         WHERE sa.business_date >= @From AND sa.business_date <= @To
           AND sa.status = 'COMPLETED'
           AND (@IncludeRolledUpDates = 1
                OR sa.business_date >= @SplitKey
                OR NOT EXISTS (SELECT 1 FROM daily_sales_summary r WHERE r.business_date = sa.business_date)
                OR EXISTS (SELECT 1 FROM sale stale
                            WHERE stale.business_date = sa.business_date
                              AND stale.status <> 'COMPLETED'));
        """;

    private const string RawReturnsSql =
        """
        SELECT COUNT(*) AS ReturnCount,
               COALESCE(SUM(sr.subtotal), 0) AS SubtotalScaled,
               COALESCE(SUM(sr.total_refund), 0) AS TotalRefundScaled
          FROM sale_return sr
         WHERE sr.business_date >= @From AND sr.business_date <= @To
           AND (@IncludeRolledUpDates = 1
                OR sr.business_date >= @SplitKey
                OR NOT EXISTS (SELECT 1 FROM daily_sales_summary r WHERE r.business_date = sr.business_date)
                OR EXISTS (SELECT 1 FROM sale stale
                            WHERE stale.business_date = sr.business_date
                              AND stale.status <> 'COMPLETED'));
        """;

    private const string RawTenderSql =
        """
        SELECT COALESCE(SUM(combined.amount), 0) AS TenderTotalScaled
          FROM (
                SELECT p.amount AS amount, sa.business_date AS business_date
                  FROM payment p
                  JOIN sale sa ON sa.id = p.sale_id
                 WHERE sa.business_date >= @From AND sa.business_date <= @To
                   AND sa.status = 'COMPLETED'
                UNION ALL
                SELECT p.amount AS amount, sr.business_date AS business_date
                  FROM payment p
                  JOIN sale_return sr ON sr.id = p.sale_return_id
                 WHERE sr.business_date >= @From AND sr.business_date <= @To
               ) combined
         WHERE (@IncludeRolledUpDates = 1
                OR combined.business_date >= @SplitKey
                OR NOT EXISTS (SELECT 1 FROM daily_sales_summary r WHERE r.business_date = combined.business_date)
                OR EXISTS (SELECT 1 FROM sale stale
                            WHERE stale.business_date = combined.business_date
                              AND stale.status <> 'COMPLETED'));
        """;

    // Only a SELLABLE line goes back onto the shelf (CreateReturnHandler posts a RETURN_IN stock
    // movement for that disposition alone), so only its cost is genuinely recovered and its
    // sale's own COGS rightly reversed. A DAMAGED line never re-enters stock and no write-off
    // movement is posted for it either - the shop has both refunded the money and lost the
    // goods - so it is excluded here entirely (DailyRollupCalculator.ComputeAsync mirrors this).
    private const string RawReturnCogsSql =
        """
        SELECT srl.unit_cost AS UnitCostScaled, srl.qty_base AS QtyBaseScaled
          FROM sale_return_line srl
          JOIN sale_return sr ON sr.id = srl.sale_return_id
         WHERE sr.business_date >= @From AND sr.business_date <= @To
           AND srl.disposition = 'SELLABLE'
           AND (@IncludeRolledUpDates = 1
                OR sr.business_date >= @SplitKey
                OR NOT EXISTS (SELECT 1 FROM daily_sales_summary r WHERE r.business_date = sr.business_date)
                OR EXISTS (SELECT 1 FROM sale stale
                            WHERE stale.business_date = sr.business_date
                              AND stale.status <> 'COMPLETED'));
        """;

    private readonly IReportConnectionFactory _connectionFactory;

    public PeriodFiguresReader(IReportConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <summary>The canonical period figures without any cost figure.</summary>
    public async Task<PeriodTotals> ReadTotalsAsync(
        ReportDateRange range,
        ReportSourcePolicy sourcePolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var slice = await ResolveSliceAsync(connection, range, sourcePolicy, cancellationToken)
                .ConfigureAwait(false);

            return await ReadTotalsCoreAsync(connection, slice, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>The canonical period figures plus COGS, read on one connection for one range.</summary>
    public async Task<(PeriodTotals Totals, Money Cogs)> ReadWithCogsAsync(
        ReportDateRange range,
        ReportSourcePolicy sourcePolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(range);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var slice = await ResolveSliceAsync(connection, range, sourcePolicy, cancellationToken)
                .ConfigureAwait(false);

            var totals = await ReadTotalsCoreAsync(connection, slice, cancellationToken).ConfigureAwait(false);
            var cogs = await ReadCogsCoreAsync(connection, slice, cancellationToken).ConfigureAwait(false);

            return (totals, cogs);
        }
    }

    /// <summary>
    /// The canonical period figures, without cost - see
    /// <see cref="Counterpoint.Application.Reporting.ISalesPeriodSummaryQuery"/>.
    /// </summary>
    private static async Task<PeriodTotals> ReadTotalsCoreAsync(
        DbConnection connection,
        ReportSlice slice,
        CancellationToken cancellationToken)
    {
        var parameters = slice.Parameters();

        var rollup = slice.UseRollups
            ? await connection.QuerySingleAsync<RollupTotalsRow>(
                new CommandDefinition(RollupTotalsSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)
            : RollupTotalsRow.Empty;

        var sales = await connection.QuerySingleAsync<RawSalesRow>(
            new CommandDefinition(RawSalesSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var returns = await connection.QuerySingleAsync<RawReturnsRow>(
            new CommandDefinition(RawReturnsSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var tenderTotalScaled = await connection.ExecuteScalarAsync<long>(
            new CommandDefinition(RawTenderSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        // gross = subtotal + line_discount; the rollup's own gross already includes its sale's
        // line discount, so the two segments simply add.
        var gross = Money.FromScaled(rollup.GrossScaled + sales.SubtotalScaled + sales.LineDiscountScaled);

        // discount = line_discount + bill_discount.
        var discounts = Money.FromScaled(rollup.DiscountScaled + sales.LineDiscountScaled + sales.BillDiscountScaled);

        var tax = Money.FromScaled(rollup.TaxScaled + sales.TaxScaled);

        // net = gross - discounts - returns, per segment. For the raw segment this reduces to
        // subtotal - bill_discount - return_subtotal, the identical algebra DailyRollupCalculator
        // stores in daily_sales_summary.net.
        var rawGross = Money.FromScaled(sales.SubtotalScaled) + Money.FromScaled(sales.LineDiscountScaled);
        var rawDiscounts = Money.FromScaled(sales.LineDiscountScaled) + Money.FromScaled(sales.BillDiscountScaled);
        var net = Money.FromScaled(rollup.NetScaled)
            + rawGross
            - rawDiscounts
            - Money.FromScaled(returns.SubtotalScaled);

        var returnsValue = Money.FromScaled(rollup.ReturnsValueScaled + returns.TotalRefundScaled);

        var tenderTotal = Money.FromScaled(rollup.TenderTotalScaled + tenderTotalScaled);

        return new PeriodTotals(
            rollup.BillCount + sales.BillCount,
            rollup.ReturnCount + returns.ReturnCount,
            gross,
            discounts,
            tax,
            net,
            returnsValue,
            tenderTotal);
    }

    /// <summary>
    /// COGS: <c>sale.cogs</c> (the moving-average cost captured on each sale header at the time of
    /// sale, CLAUDE.md invariant 10) plus the raw segment's own, minus the returned equivalent. The
    /// returned equivalent is <c>unit_cost * qty_base</c> on <c>sale_return_line</c>, multiplied in
    /// C# through <see cref="Money"/> exactly as <c>DailyRollupCalculator.LineCogs</c> does - never
    /// <c>unit_cost_scaled * qty_base_scaled</c> in SQL, which would double-scale.
    /// </summary>
    private static async Task<Money> ReadCogsCoreAsync(
        DbConnection connection,
        ReportSlice slice,
        CancellationToken cancellationToken)
    {
        var parameters = slice.Parameters();

        var rollupCogsScaled = slice.UseRollups
            ? await connection.ExecuteScalarAsync<long>(
                new CommandDefinition(RollupCogsSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false)
            : 0L;

        var rawSales = await connection.QuerySingleAsync<RawSalesRow>(
            new CommandDefinition(RawSalesSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var returnLines = await connection.QueryAsync<ReturnCogsRow>(
            new CommandDefinition(RawReturnCogsSql, parameters, cancellationToken: cancellationToken)).ConfigureAwait(false);

        var returnCogs = Money.Zero;
        foreach (var line in returnLines)
        {
            returnCogs += Money.FromScaled(line.UnitCostScaled)
                * Quantity.FromScaled(line.QtyBaseScaled, uomId: 0).Value;
        }

        return Money.FromScaled(rollupCogsScaled + rawSales.CogsScaled) - returnCogs;
    }

    /// <summary>
    /// Resolves the routing split: the open shift's business date (or a sentinel past the range when
    /// no shift is open), and whether rollups may be read at all.
    /// </summary>
    private static async Task<ReportSlice> ResolveSliceAsync(
        DbConnection connection,
        ReportDateRange range,
        ReportSourcePolicy sourcePolicy,
        CancellationToken cancellationToken)
    {
        var fromText = Text(range.From);
        var toText = Text(range.To);

        if (sourcePolicy == ReportSourcePolicy.RawTablesRequired)
        {
            // No rollup segment: every date is in the raw range, so the split key is never consulted
            // (IncludeRolledUpDates = 1 short-circuits the predicate) and the open shift is not read.
            return new ReportSlice(
                UseRollups: false,
                FromText: fromText,
                ToText: toText,
                SplitKeyText: fromText,
                IncludeRolledUpDates: 1);
        }

        var openShiftDate = await connection.QuerySingleOrDefaultAsync<string>(
            new CommandDefinition(OpenShiftDateSql, cancellationToken: cancellationToken)).ConfigureAwait(false);

        // With no open shift every business date is closed and belongs to the rollup segment; the
        // sentinel one day past the range makes the raw segment empty.
        var splitKey = openShiftDate is null
            ? Text(range.To.AddDays(1))
            : openShiftDate;

        return new ReportSlice(
            UseRollups: true,
            FromText: fromText,
            ToText: toText,
            SplitKeyText: splitKey,
            IncludeRolledUpDates: 0);
    }

    private static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>
    /// The resolved routing split, and the parameter set every statement below shares.
    /// </summary>
    private readonly record struct ReportSlice(
        bool UseRollups,
        string FromText,
        string ToText,
        string SplitKeyText,
        int IncludeRolledUpDates)
    {
        internal object Parameters() => new
        {
            From = FromText,
            To = ToText,
            SplitKey = SplitKeyText,
            IncludeRolledUpDates,
        };
    }

    /// <summary>The flat shape Dapper maps <see cref="RollupTotalsSql"/> onto.</summary>
    private sealed class RollupTotalsRow
    {
        internal static readonly RollupTotalsRow Empty = new();

        public int BillCount { get; set; }

        public long GrossScaled { get; set; }

        public long DiscountScaled { get; set; }

        public long TaxScaled { get; set; }

        public long NetScaled { get; set; }

        public int ReturnCount { get; set; }

        public long ReturnsValueScaled { get; set; }

        public long TenderTotalScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps <see cref="RawSalesSql"/> onto.</summary>
    private sealed class RawSalesRow
    {
        public int BillCount { get; set; }

        public long SubtotalScaled { get; set; }

        public long LineDiscountScaled { get; set; }

        public long BillDiscountScaled { get; set; }

        public long TaxScaled { get; set; }

        public long CogsScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps <see cref="RawReturnsSql"/> onto.</summary>
    private sealed class RawReturnsRow
    {
        public int ReturnCount { get; set; }

        public long SubtotalScaled { get; set; }

        public long TotalRefundScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps <see cref="RawReturnCogsSql"/> onto.</summary>
    private sealed class ReturnCogsRow
    {
        public long UnitCostScaled { get; set; }

        public long QtyBaseScaled { get; set; }
    }
}

/// <summary>
/// The canonical period figures without any cost figure - the internal shape both public query DTOs
/// are projected from (task P3-T04 "Do this" #2).
/// </summary>
internal sealed record PeriodTotals(
    int BillCount,
    int ReturnCount,
    Money GrossSales,
    Money Discounts,
    Money Tax,
    Money NetSales,
    Money ReturnsValue,
    Money TenderTotal);
