using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.Pricing;

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
    /// <param name="lines">The lines on the bill so far.</param>
    /// <param name="billDiscount">
    /// A whole-bill discount the cashier asked for (SRS FR-3.17), or null for none. Capped by the
    /// shop's bill-discount limit (SRS FR-3.18); a request above the cap is refused (no owner
    /// override path exists in this task).
    /// </param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="System.InvalidOperationException">
    /// A line names a variant that is not in the catalogue, a quantity that is not positive, an
    /// open item without a description or price, or a discount above its cap.
    /// </exception>
    public Task<SaleQuote> QuoteAsync(
        IReadOnlyList<SaleLineRequest> lines,
        DiscountInput? billDiscount = null,
        CancellationToken cancellationToken = default);
}
