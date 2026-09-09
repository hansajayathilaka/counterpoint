using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// What the cashier's screen learns when a barcode is scanned.
/// </summary>
/// <remarks>
/// <b>There is no cost field, and that is the design.</b> Cost, margin and profit are excluded
/// at the query projection level for a cashier session, so there is nothing in the object that
/// reaches the UI to leak (CLAUDE.md invariant 8, SRS NFR-S2, AC-17). The cost that
/// <c>sale_line</c> snapshots is read separately, at completion, and never crosses this
/// boundary.
/// </remarks>
/// <param name="ProductVariantId">The variant to put on the bill.</param>
/// <param name="Description">The name to show and to snapshot.</param>
/// <param name="UomId">The unit the line is priced in by default - the product's base unit.</param>
/// <param name="UomSymbol">That unit's symbol.</param>
/// <param name="UnitPrice">Price per unit.</param>
/// <param name="QtyOnHand">
/// Stock on hand, in the base unit (SRS FR-3.11 - "the system must show live stock-on-hand for
/// the scanned item on screen").
/// </param>
/// <param name="MaxDiscountRate">
/// <c>product.max_discount_rate</c>, or null when the product has none and the shop's cashier
/// limit applies instead (SRS FR-3.18, Q-12). Not cost or margin - a policy ceiling, safe for a
/// cashier session to see.
/// </param>
/// <param name="UnitOptions">
/// Every unit this item may be sold in, base unit included, each already priced (SRS FR-2.5,
/// FR-3.7) - what lets the sales screen offer a unit switch without a second round trip.
/// </param>
public sealed record ScannedItem(
    long ProductVariantId,
    string Description,
    long UomId,
    string UomSymbol,
    Money UnitPrice,
    Quantity QtyOnHand,
    Percentage? MaxDiscountRate,
    IReadOnlyList<ScannedItemUnitOption> UnitOptions);

/// <summary>
/// One unit a scanned item may be sold in, already priced (SRS FR-2.5, FR-3.7).
/// </summary>
/// <param name="UomId">The unit.</param>
/// <param name="Symbol">Its symbol.</param>
/// <param name="UnitPrice">What one of this unit sells for.</param>
/// <param name="IsBase">True for the product's base unit.</param>
public sealed record ScannedItemUnitOption(long UomId, string Symbol, Money UnitPrice, bool IsBase);
