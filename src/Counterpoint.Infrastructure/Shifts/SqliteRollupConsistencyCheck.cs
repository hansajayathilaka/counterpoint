using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Abstractions.Persistence;
using Counterpoint.Domain.ValueObjects;
using Counterpoint.Infrastructure.Data;
using Dapper;

namespace Counterpoint.Infrastructure.Shifts;

/// <summary>
/// <see cref="IRollupConsistencyCheck"/>: recomputes one business date through the same
/// <see cref="DailyRollupCalculator"/> <see cref="SqliteDailyRollupBuilder"/> writes from, and
/// compares it against the stored <c>daily_sales_summary</c> row and every stored
/// <c>daily_product_summary</c> row for that date - without writing anything (task P3-T03's own
/// "Risks": "add a rollup-verification command that recomputes and compares, run monthly", which
/// names both rollup tables this task builds, not the day header alone).
/// </summary>
internal sealed class SqliteRollupConsistencyCheck : IRollupConsistencyCheck
{
    private const string StoredRowSql =
        """
        SELECT bill_count AS BillCount,
               gross AS GrossScaled,
               discount AS DiscountScaled,
               tax AS TaxScaled,
               net AS NetScaled,
               cogs AS CogsScaled,
               return_count AS ReturnCount,
               return_value AS ReturnValueScaled,
               tender_cash AS TenderCashScaled,
               tender_card AS TenderCardScaled,
               tender_other AS TenderOtherScaled
          FROM daily_sales_summary
         WHERE business_date = @BusinessDate;
        """;

    private const string StoredProductRowsSql =
        """
        SELECT product_variant_id AS ProductVariantId,
               qty_base AS QtyBaseScaled,
               net AS NetScaled,
               cogs AS CogsScaled
          FROM daily_product_summary
         WHERE business_date = @BusinessDate;
        """;

    private readonly IPosConnectionFactory _connectionFactory;

    public SqliteRollupConsistencyCheck(IPosConnectionFactory connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        _connectionFactory = connectionFactory;
    }

    /// <inheritdoc />
    public async Task<RollupConsistencyReport> CheckAsync(
        DateOnly businessDate, CancellationToken cancellationToken = default)
    {
        var dateText = businessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var connection = await _connectionFactory.OpenReadConnectionAsync(cancellationToken).ConfigureAwait(false);
        DailySalesSummaryFigures? stored;
        IReadOnlyList<StoredProductRow> storedProductRows;
        DailyRollupComputation recomputed;

        await using (connection.ConfigureAwait(false))
        {
            var row = await connection.QuerySingleOrDefaultAsync<StoredRow>(
                new CommandDefinition(StoredRowSql, new { BusinessDate = dateText }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);

            stored = row is null ? null : ToFigures(row);

            var products = await connection.QueryAsync<StoredProductRow>(
                new CommandDefinition(StoredProductRowsSql, new { BusinessDate = dateText }, cancellationToken: cancellationToken))
                .ConfigureAwait(false);
            storedProductRows = products.AsList();

            recomputed = await DailyRollupCalculator
                .ComputeAsync(connection, transaction: null, businessDate, cancellationToken)
                .ConfigureAwait(false);
        }

        var recomputedFigures = ToFigures(recomputed);
        var summaryMatches = stored is not null && stored == recomputedFigures;

        var storedProducts = storedProductRows.ToDictionary(
            row => row.ProductVariantId,
            ToFigures);
        var recomputedProducts = recomputed.Products.ToDictionary(
            product => product.ProductVariantId,
            ToFigures);

        var productMismatches = storedProducts.Keys
            .Union(recomputedProducts.Keys)
            .OrderBy(variantId => variantId)
            .Select(variantId => new
            {
                VariantId = variantId,
                Stored = storedProducts.GetValueOrDefault(variantId),
                Recomputed = recomputedProducts.GetValueOrDefault(variantId),
            })
            .Where(candidate => candidate.Stored != candidate.Recomputed)
            .Select(candidate => new DailyProductRollupMismatch(candidate.VariantId, candidate.Stored, candidate.Recomputed))
            .ToList();

        var matches = summaryMatches && productMismatches.Count == 0;

        return new RollupConsistencyReport(
            businessDate, stored is not null, matches, stored, recomputedFigures, productMismatches);
    }

    private static DailySalesSummaryFigures ToFigures(StoredRow row) => new(
        row.BillCount,
        Money.FromScaled(row.GrossScaled),
        Money.FromScaled(row.DiscountScaled),
        Money.FromScaled(row.TaxScaled),
        Money.FromScaled(row.NetScaled),
        Money.FromScaled(row.CogsScaled),
        row.ReturnCount,
        Money.FromScaled(row.ReturnValueScaled),
        Money.FromScaled(row.TenderCashScaled),
        Money.FromScaled(row.TenderCardScaled),
        Money.FromScaled(row.TenderOtherScaled));

    private static DailySalesSummaryFigures ToFigures(DailyRollupComputation computation) => new(
        computation.BillCount,
        computation.Gross,
        computation.Discount,
        computation.Tax,
        computation.Net,
        computation.Cogs,
        computation.ReturnCount,
        computation.ReturnValue,
        computation.TenderCash,
        computation.TenderCard,
        computation.TenderOther);

    private static DailyProductRollupFigures ToFigures(StoredProductRow row) => new(
        row.QtyBaseScaled,
        Money.FromScaled(row.NetScaled),
        Money.FromScaled(row.CogsScaled));

    private static DailyProductRollupFigures ToFigures(DailyProductRollup product) => new(
        product.QtyBaseScaled,
        product.Net,
        product.Cogs);

    /// <summary>The flat shape Dapper maps a row of <see cref="StoredRowSql"/> onto.</summary>
    private sealed class StoredRow
    {
        public int BillCount { get; set; }

        public long GrossScaled { get; set; }

        public long DiscountScaled { get; set; }

        public long TaxScaled { get; set; }

        public long NetScaled { get; set; }

        public long CogsScaled { get; set; }

        public int ReturnCount { get; set; }

        public long ReturnValueScaled { get; set; }

        public long TenderCashScaled { get; set; }

        public long TenderCardScaled { get; set; }

        public long TenderOtherScaled { get; set; }
    }

    /// <summary>The flat shape Dapper maps a row of <see cref="StoredProductRowsSql"/> onto.</summary>
    private sealed class StoredProductRow
    {
        public long ProductVariantId { get; set; }

        public long QtyBaseScaled { get; set; }

        public long NetScaled { get; set; }

        public long CogsScaled { get; set; }
    }
}
