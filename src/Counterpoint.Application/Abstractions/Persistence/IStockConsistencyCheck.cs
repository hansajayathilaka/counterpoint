using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Samples the balance projection against the ledger it is supposed to be a cache of
/// (P1-T07, SAD §3).
/// </summary>
/// <remarks>
/// Diagnostic, never corrective: a mismatch is logged loudly and reported for a dashboard to
/// flag; fixing it is <see cref="IRebuildStockBalance"/>'s job, run deliberately, never
/// automatically.
/// </remarks>
public interface IStockConsistencyCheck
{
    /// <summary>
    /// Compares a random sample of variants' projected balance against the sum of their ledger
    /// movements.
    /// </summary>
    /// <param name="sampleSize">How many variants to sample. Defaults to 200 (SAD §3).</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    public Task<StockConsistencyReport> CheckAsync(
        int sampleSize = 200,
        CancellationToken cancellationToken = default);
}

/// <summary>One variant whose projection did not match its ledger.</summary>
/// <param name="ProductVariantId">The variant.</param>
/// <param name="ProjectedQtyBase">The balance projection's quantity, in base units.</param>
/// <param name="LedgerQtyBase">The sum of the ledger's movements, in base units.</param>
public sealed record StockConsistencyMismatch(long ProductVariantId, decimal ProjectedQtyBase, decimal LedgerQtyBase);

/// <summary>The result of one consistency check.</summary>
/// <param name="SampledCount">How many variants were sampled.</param>
/// <param name="Mismatches">Every sampled variant whose projection did not match its ledger. Empty when clean.</param>
public sealed record StockConsistencyReport(int SampledCount, IReadOnlyList<StockConsistencyMismatch> Mismatches)
{
    /// <summary>True when at least one sampled variant disagreed with its ledger.</summary>
    public bool HasMismatch => Mismatches.Count > 0;
}
