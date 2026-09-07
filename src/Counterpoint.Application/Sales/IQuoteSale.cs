using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Prices the bill being built, without writing anything (SRS FR-3.10).
/// </summary>
/// <remarks>
/// This exists so the UI never multiplies a price by a quantity. The running total on the
/// screen, the total the cashier reads out, and the total the tender must match are all one
/// number computed in one place - which is also the only place rounding is allowed to happen
/// (CLAUDE.md invariant 2).
/// </remarks>
public interface IQuoteSale
{
    /// <summary>
    /// Prices the given lines at today's catalogue prices.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// A line names a variant that is not in the catalogue, or a quantity that is not positive.
    /// </exception>
    public Task<SaleQuote> QuoteAsync(
        IReadOnlyList<SaleLineRequest> lines,
        CancellationToken cancellationToken = default);
}
