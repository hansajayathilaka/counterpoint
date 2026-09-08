using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One movement of stock, in base units, signed: positive increases what is on the shelf.
/// </summary>
/// <param name="ProductVariantId">The variant that moved.</param>
/// <param name="MovementType">A <c>stock_movement.movement_type</c> value, for example <c>SALE</c>.</param>
/// <param name="QuantityBase">Signed amount, in the product's base unit.</param>
/// <param name="UnitCost">
/// Cost per base unit at the moment of the movement. Meaningful only when
/// <paramref name="QuantityBase"/> is positive (inbound) - it is what the moving-average cost
/// recomputes against. For an outbound movement the ledger ignores this value entirely and
/// records the current moving-average cost instead, as the COGS snapshot (P1-T07); pass
/// whatever is convenient, for example the catalogue's own cost, since it is never read.
/// </param>
/// <param name="RefDocType">What caused it, for example <c>SALE</c>.</param>
/// <param name="RefDocId">The id of that document.</param>
/// <param name="UserId">Who caused it.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="Note">Free text, for an adjustment or a damage write-off.</param>
public sealed record StockPosting(
    long ProductVariantId,
    string MovementType,
    Quantity QuantityBase,
    Money UnitCost,
    string RefDocType,
    long? RefDocId,
    long UserId,
    DateTimeOffset OccurredAt,
    string? Note = null);
