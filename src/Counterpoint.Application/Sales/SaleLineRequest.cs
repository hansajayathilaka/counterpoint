namespace Counterpoint.Application.Sales;

/// <summary>
/// One line the cashier has put on the bill: what, and how much of it.
/// </summary>
/// <remarks>
/// No price and no cost. The price charged and the cost snapshotted are both read from the
/// catalogue at completion, inside the Application layer - a UI that could name its own price
/// would be a manual price override, which is an owner-only, audited action (SRS FR-3.19) and
/// belongs to P1-T08.
/// </remarks>
/// <param name="ProductVariantId">The variant, as returned by <see cref="IScanItem"/>.</param>
/// <param name="Quantity">
/// How much, in the product's base unit. Selling in a different unit (box, coil) is
/// FR-3.7 and arrives with UOM conversion in P1-T05.
/// </param>
public sealed record SaleLineRequest(long ProductVariantId, decimal Quantity);
