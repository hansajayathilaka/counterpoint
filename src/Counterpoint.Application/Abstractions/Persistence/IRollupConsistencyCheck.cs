using System;
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
/// True when every figure on the stored row equals the same figure recomputed fresh from
/// <c>sale</c>, <c>sale_line</c>, <c>sale_return</c> and <c>sale_return_line</c>. False when the
/// stored row exists but disagrees, or when it does not exist but the raw tables show trading on
/// this date (both are a drift worth reporting).
/// </param>
/// <param name="Stored">The row as currently stored, or null when there is none.</param>
/// <param name="Recomputed">The row a fresh recomputation from raw data would produce.</param>
public sealed record RollupConsistencyReport(
    DateOnly BusinessDate,
    bool RowExists,
    bool Matches,
    DailySalesSummaryFigures? Stored,
    DailySalesSummaryFigures Recomputed);

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
