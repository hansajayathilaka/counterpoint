using System.Collections.Generic;

namespace Counterpoint.Application.Sales;

/// <summary>
/// The in-progress bill, as it is held (SRS FR-3.32, FR-3.33) and recalled.
/// </summary>
/// <remarks>
/// A plain, JSON-friendly shape - decimals and longs, not <c>Money</c>, <c>Quantity</c> or
/// <c>DiscountInput</c> - because those value objects are built from private constructors and a
/// static factory, not from a public parameterless one, so a general-purpose JSON serialiser
/// cannot round-trip them faithfully. This is the one place a hold/recall converts to and from
/// the richer <see cref="SaleLineRequest"/> shape (SqliteHeldBillService), the same reasoning
/// <c>SecurityAuditJson</c> and <c>AuditPayload</c> already apply to audit rows elsewhere.
/// </remarks>
/// <param name="CustomerId">The attached customer, if any (SRS FR-3.22).</param>
/// <param name="BillDiscountIsRate">Whether <see cref="BillDiscountValue"/> is a percentage or a fixed amount.</param>
/// <param name="BillDiscountValue">
/// The whole-bill discount as typed - a fraction (0.10 for 10%) when <see cref="BillDiscountIsRate"/>,
/// otherwise a currency amount. Null when no bill discount was in force.
/// </param>
/// <param name="Lines">The bill's lines, in order.</param>
public sealed record HeldBillPayload(
    long? CustomerId,
    bool BillDiscountIsRate,
    decimal? BillDiscountValue,
    IReadOnlyList<HeldBillLinePayload> Lines);

/// <summary>One line of a held bill (SRS FR-3.32 - "preserve lines, quantities, discounts").</summary>
/// <param name="ProductVariantId">The variant, or null for an open item (SRS FR-2.8).</param>
/// <param name="Quantity">How much, in <see cref="UomId"/>'s unit.</param>
/// <param name="UomId">The unit sold in, or null for a catalogue line's default (its base unit).</param>
/// <param name="OpenItemDescription">The open item's description, or null.</param>
/// <param name="OpenItemUnitPrice">The open item's price, or null.</param>
/// <param name="DiscountIsRate">Whether <see cref="DiscountValue"/> is a percentage or a fixed amount.</param>
/// <param name="DiscountValue">The line discount as typed, or null when there is none.</param>
public sealed record HeldBillLinePayload(
    long? ProductVariantId,
    decimal Quantity,
    long? UomId,
    string? OpenItemDescription,
    decimal? OpenItemUnitPrice,
    bool DiscountIsRate,
    decimal? DiscountValue);
