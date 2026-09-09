using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One variant a bulk price update might touch (SRS FR-2.19), with what
/// <c>Counterpoint.Application.Pricing.IBulkPriceUpdateService</c> needs to preview and apply an
/// adjustment: today's price, and the product's cost for the below-cost warning (FR-2.18). Owner
/// only, the same as every other read that carries cost (CLAUDE.md invariant 8).
/// </summary>
public sealed record PriceQueryVariant(
    long ProductVariantId,
    string ProductName,
    string Sku,
    Money Price,
    Money Cost);
