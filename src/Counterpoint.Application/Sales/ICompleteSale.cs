using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Sales;

/// <summary>
/// The one way a bill gets completed (SRS FR-3.28, AC-02).
/// </summary>
/// <remarks>
/// The viewmodel depends on this interface and on nothing else about the sale path. Everything
/// that decides whether a sale is allowed, what it costs and what it moves lives behind it, in
/// the Application layer - never in the UI (CLAUDE.md invariant 8, SRS NFR-S2).
/// </remarks>
public interface ICompleteSale
{
    /// <summary>
    /// Completes the bill as one transaction, or changes nothing at all (SRS FR-3.30).
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The bill does not add up - an empty bill, an unknown variant, or tenders that do not sum
    /// to the total. It refuses rather than silently correcting.
    /// </exception>
    public Task<CompletedSale> CompleteAsync(
        CompleteSaleCommand command,
        CancellationToken cancellationToken = default);
}
