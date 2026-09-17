using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Recomputes one business date's rollup from the raw sales and returns tables and compares it
/// against the stored <c>daily_sales_summary</c> row, without writing anything (task P3-T03's own
/// "Risks": "add a rollup-verification command that recomputes and compares, run monthly").
/// </summary>
/// <remarks>
/// Diagnostic, never corrective - the same split <see cref="IStockConsistencyCheck"/> draws between
/// itself and <see cref="IRebuildStockBalance"/>: a mismatch here is reported for a human to look
/// at; fixing it is <see cref="IDailyRollupBuilder.RebuildAsync"/>'s job, run deliberately, not
/// automatically from inside a check.
/// </remarks>
public interface IRollupConsistencyCheck
{
    /// <summary>Compares one business date's stored rollup against a fresh recomputation.</summary>
    /// <param name="businessDate">The date to check.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    public Task<RollupConsistencyReport> CheckAsync(
        DateOnly businessDate,
        CancellationToken cancellationToken = default);
}

/// <summary>The result of one rollup consistency check (task P3-T03's own "Risks").</summary>
/// <param name="BusinessDate">The date checked.</param>
/// <param name="RowExists">Whether <c>daily_sales_summary</c> has a row for this date at all.</param>
/// <param name="Matches">
/// True when every figure on the stored <c>daily_sales_summary</c> row equals the same figure
/// recomputed fresh from <c>sale</c>, <c>sale_line</c>, <c>sale_return</c> and
/// <c>sale_return_line</c>, AND every <c>daily_product_summary</c> row for this date agrees with
/// the same recomputation (<see cref="ProductMismatches"/> is empty). False when the stored
/// <c>daily_sales_summary</c> row disagrees or is missing while the raw tables show trading on
/// this date, or when any per-product row is missing, extra, or disagrees - a per-product
/// attribution bug (two variants merged into one row, or a variant's row silently dropped) would
/// otherwise sail through undetected, since the day-header aggregate alone cannot reveal it.
/// </param>
/// <param name="Stored">The <c>daily_sales_summary</c> row as currently stored, or null when there is none.</param>
/// <param name="Recomputed">The <c>daily_sales_summary</c> row a fresh recomputation from raw data would produce.</param>
/// <param name="ProductMismatches">
/// Every <c>daily_product_summary</c> row (keyed by <c>product_variant_id</c>) where the stored
/// row and the fresh recomputation disagree, including a variant present on only one side. Empty
/// when every per-product row for this date matches exactly.
/// </param>
public sealed record RollupConsistencyReport(
    DateOnly BusinessDate,
    bool RowExists,
    bool Matches,
    DailySalesSummaryFigures? Stored,
    DailySalesSummaryFigures Recomputed,
    IReadOnlyList<DailyProductRollupMismatch> ProductMismatches);

/// <summary>
/// One business date's <c>daily_sales_summary</c> figures, either as stored or as recomputed
/// (task P3-T03 "Do this" #2).
/// </summary>
public sealed record DailySalesSummaryFigures(
    int BillCount,
    Money Gross,
    Money Discount,
    Money Tax,
    Money Net,
    Money Cogs,
    int ReturnCount,
    Money ReturnValue,
    Money TenderCash,
    Money TenderCard,
    Money TenderOther);

/// <summary>
/// One product variant's <c>daily_product_summary</c> figures for a business date, either as
/// stored or as recomputed (task P3-T03 "Do this" #2's per-product rollup, the same
/// <c>DailyRollupCalculator.ComputeAsync(...).Products</c> list <c>SqliteDailyRollupBuilder</c>
/// writes from).
/// </summary>
public sealed record DailyProductRollupFigures(long QtyBase, Money Net, Money Cogs);

/// <summary>
/// One product variant's rollup where the stored <c>daily_product_summary</c> row and a fresh
/// recomputation from raw data disagree (task P3-T03's own "Risks": a rollup-verification tool
/// that would actually catch a per-product attribution bug, not just a day-header one).
/// </summary>
/// <param name="ProductVariantId">The variant this row is keyed on.</param>
/// <param name="Stored">
/// The figures as currently stored, or null when the raw data trades this variant on this date
/// but no stored row exists for it (a dropped row).
/// </param>
/// <param name="Recomputed">
/// The figures a fresh recomputation would produce, or null when a stored row exists for this
/// variant but the raw data no longer supports it on this date (a stale/extra row).
/// </param>
public sealed record DailyProductRollupMismatch(
    long ProductVariantId,
    DailyProductRollupFigures? Stored,
    DailyProductRollupFigures? Recomputed);
