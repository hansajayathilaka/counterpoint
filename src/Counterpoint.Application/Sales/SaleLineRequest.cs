using System;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// One line the cashier has put on the bill: what, and how much of it.
/// </summary>
/// <remarks>
/// No unit price for a catalogue line, and no cost anywhere. The price charged and the cost
/// snapshotted are both read from the catalogue at completion, inside the Application layer - a
/// UI that could name its own price for a catalogue item would be a manual price override, which
/// is an owner-only, audited action (SRS FR-3.19) this task does not build (no re-authentication
/// dialog exists yet to gate it). An <see cref="OpenItemUnitPrice"/> is the one place a price is
/// typed in, because an open item (SRS FR-2.8) has no catalogue price to read.
/// </remarks>
/// <param name="ProductVariantId">
/// The variant, as returned by <see cref="IScanItem"/>, or null for an open item
/// (SRS FR-2.8) - a manually described, manually priced line for something not in the catalogue.
/// Exactly one of a catalogue variant or <see cref="OpenItemDescription"/>/<see cref="OpenItemUnitPrice"/>
/// applies to a line; <c>CompleteSaleHandler</c> refuses a request that names both or neither.
/// </param>
/// <param name="Quantity">
/// How much, in the unit named by <see cref="UomId"/> (or the product's base unit, when
/// <see cref="UomId"/> is null and this is a catalogue line).
/// </param>
/// <param name="UomId">
/// The unit the cashier is selling this line in (SRS FR-2.5, FR-3.7 - switching piece, box or
/// coil recalculates price and stock impact through the conversion factor). Null on a catalogue
/// line means the product's base unit. Required on an open item, because
/// <c>sale_line.uom_id</c> is not nullable and an open item has no product to default it from.
/// </param>
/// <param name="OpenItemDescription">The manually typed description of an open item (SRS FR-2.8).</param>
/// <param name="OpenItemUnitPrice">The manually typed price of an open item, per <see cref="Quantity"/>'s unit.</param>
/// <param name="Discount">
/// A line discount the cashier asked for (SRS FR-3.16), as a percentage or a fixed amount. Null
/// means no discount. Capped by the product's own limit if it has one, otherwise the shop's
/// cashier limit (SRS FR-3.18, Q-12); a request above the cap is refused; there is no owner
/// override path in this task; see <see cref="Counterpoint.Application.Pricing.DiscountLimitExceededException"/>.
/// </param>
public sealed record SaleLineRequest(
    long? ProductVariantId,
    decimal Quantity,
    long? UomId = null,
    string? OpenItemDescription = null,
    Money? OpenItemUnitPrice = null,
    DiscountInput? Discount = null)
{
    /// <summary>True when this line names no catalogue variant - an open item (SRS FR-2.8).</summary>
    public bool IsOpenItem => ProductVariantId is null;

    /// <summary>
    /// Refuses a line that is neither a catalogue reference nor a complete open item, or that
    /// tries to be both at once.
    /// </summary>
    /// <exception cref="InvalidOperationException">The line is malformed.</exception>
    public void RequireWellFormed()
    {
        if (Quantity <= 0m)
        {
            throw new InvalidOperationException(
                "A bill line must have a positive quantity. Removing an item is not a negative line.");
        }

        if (IsOpenItem)
        {
            if (string.IsNullOrWhiteSpace(OpenItemDescription))
            {
                throw new InvalidOperationException(
                    "An open item needs a description - there is no catalogue entry to read one from.");
            }

            if (OpenItemUnitPrice is not { } price || price.IsNegative)
            {
                throw new InvalidOperationException(
                    "An open item needs a price of zero or more - there is no catalogue entry to read one from.");
            }

            if (UomId is null)
            {
                throw new InvalidOperationException(
                    "An open item needs a unit - there is no product to default it from.");
            }
        }
        else if (OpenItemDescription is not null || OpenItemUnitPrice is not null)
        {
            throw new InvalidOperationException(
                "A line naming a catalogue variant cannot also carry an open item's description or price.");
        }
    }
}
