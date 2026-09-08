using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// What <see cref="PriceResolver.Resolve"/> decided a unit sells for, and why (SRS FR-2.13,
/// task P1-T08: "return the price and the reason, so the UI can show why").
/// </summary>
/// <param name="Price">
/// What one of the requested unit sells for - already converted by the unit's conversion
/// factor, ready to multiply by the quantity sold.
/// </param>
/// <param name="Basis">Which precedence level won.</param>
/// <param name="PriceTierId">
/// The <c>price_tier.id</c> the price came from, when <see cref="Basis"/> is
/// <see cref="PriceBasis.Promotional"/>, <see cref="PriceBasis.QuantityBreak"/> or
/// <see cref="PriceBasis.Tier"/>. Null for the two levels that have no <c>price_tier</c> row.
/// </param>
/// <param name="Reason">
/// A plain-language explanation of <see cref="Basis"/>, for the cashier's screen - "the till
/// shows why", not just a code the cashier has to already know.
/// </param>
public sealed record PriceResolution(
    Money Price,
    PriceBasis Basis,
    long? PriceTierId,
    string Reason);
