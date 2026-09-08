using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Domain.Pricing;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Reads <c>price_tier</c> for one variant, the data <c>Counterpoint.Domain.Pricing.PriceResolver</c>
/// resolves promotional, quantity-break and tier prices from (docs/01_DATA_MODEL.md §3, SRS
/// FR-2.14-FR-2.16, task P1-T08).
/// </summary>
/// <remarks>
/// Read-only. Maintaining <c>price_tier</c> rows - an owner screen for trade prices, quantity
/// bands and promotions - is P5-T01's "activates the trade and quantity-break levels"; this port
/// is what makes the resolver usable against real data before that screen exists, not a
/// substitute for it.
/// </remarks>
public interface IPriceTierQuery
{
    /// <summary>Every <c>price_tier</c> row for <paramref name="productVariantId"/>, in any order.</summary>
    public Task<IReadOnlyList<PriceTierCandidate>> ListForVariantAsync(
        long productVariantId,
        CancellationToken cancellationToken = default);
}
