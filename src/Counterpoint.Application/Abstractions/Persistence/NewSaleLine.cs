using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One line of a bill about to be written.
/// </summary>
/// <remarks>
/// <see cref="Description"/>, <see cref="UnitPrice"/> and <see cref="UnitCost"/> are
/// deliberate snapshots, not lookups (CLAUDE.md invariant 10): a return refunds the price
/// originally paid and a margin report uses the cost at the time of sale, and neither is
/// recoverable from a catalogue that has moved on.
/// </remarks>
/// <param name="LineNo">1-based position on the bill.</param>
/// <param name="ProductVariantId">The variant sold. Null would be an open item; the skeleton has none.</param>
/// <param name="Description">The name as sold.</param>
/// <param name="Quantity">How much, in the unit it was sold in.</param>
/// <param name="QuantityBase">The same amount converted to the product's base unit.</param>
/// <param name="UnitPrice">Price per <see cref="Quantity"/>'s unit, as charged.</param>
/// <param name="Discount">Discount on this line.</param>
/// <param name="TaxRate">The rate charged, snapshotted.</param>
/// <param name="Tax">Tax on this line.</param>
/// <param name="LineTotal">The rounded line total - rounding point one.</param>
/// <param name="UnitCost">Cost per base unit at the moment of sale. Owner-only information.</param>
public sealed record NewSaleLine(
    int LineNo,
    long? ProductVariantId,
    string Description,
    Quantity Quantity,
    Quantity QuantityBase,
    Money UnitPrice,
    Money Discount,
    TaxRate TaxRate,
    Money Tax,
    Money LineTotal,
    Money UnitCost);
