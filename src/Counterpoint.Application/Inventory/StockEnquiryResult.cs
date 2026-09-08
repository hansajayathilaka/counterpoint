using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// What the stock enquiry screen shows for one variant (F11, SRS FR-4).
/// </summary>
/// <param name="ProductVariantId">The variant enquired about.</param>
/// <param name="Description">The product's name.</param>
/// <param name="Sku">The variant's SKU.</param>
/// <param name="BaseUomId">The product's base unit - the unit <see cref="QtyBase"/> is in.</param>
/// <param name="BaseUomSymbol">That unit's symbol.</param>
/// <param name="QtyBase">Current quantity on hand, in the base unit. May be negative (Q-11).</param>
/// <param name="CostAvg">
/// Current moving-average cost, or null. Always null for a cashier session (CLAUDE.md
/// invariant 8) and also null when the variant has never moved.
/// </param>
/// <param name="UpdatedAt">When the projection was last written, or null when it has never moved.</param>
/// <param name="AlternateUnits"><see cref="QtyBase"/> expressed in every other unit the product sells in.</param>
/// <param name="RecentMovements">The variant's most recent ledger rows, newest first.</param>
public sealed record StockEnquiryResult(
    long ProductVariantId,
    string Description,
    string Sku,
    long BaseUomId,
    string BaseUomSymbol,
    decimal QtyBase,
    Money? CostAvg,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<StockEnquiryUnitQuantity> AlternateUnits,
    IReadOnlyList<StockEnquiryMovement> RecentMovements);

/// <summary>One alternate unit's equivalent of the variant's current quantity.</summary>
/// <param name="UomId">The unit.</param>
/// <param name="Symbol">Its symbol.</param>
/// <param name="Quantity">The current base quantity, converted into this unit.</param>
public sealed record StockEnquiryUnitQuantity(long UomId, string Symbol, decimal Quantity);

/// <summary>One row of a variant's recent movement history, as the enquiry screen shows it.</summary>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="MovementType">For example <c>SALE</c> or <c>GRN</c>.</param>
/// <param name="QtyBase">Signed quantity, in base units.</param>
/// <param name="RefDocType">What caused it.</param>
/// <param name="RefDocId">The id of that document, or null.</param>
/// <param name="UnitCost">The cost recorded on the movement. Null for a cashier session.</param>
public sealed record StockEnquiryMovement(
    DateTimeOffset OccurredAt,
    string MovementType,
    decimal QtyBase,
    string RefDocType,
    long? RefDocId,
    Money? UnitCost);
