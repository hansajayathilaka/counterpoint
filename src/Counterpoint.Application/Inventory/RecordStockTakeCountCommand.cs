using System;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// Record one line's physical count (<see cref="IStockTakeService.RecordCountAsync"/>, task
/// P2-T10). On-screen entry or a scanner both end here with the same shape: a variant and how
/// many of it are actually on the shelf.
/// </summary>
/// <param name="StockTakeId">The stock take the line belongs to. Must be <c>OPEN</c>.</param>
/// <param name="ProductVariantId">The variant counted.</param>
/// <param name="CountedQty">
/// The physical count, in the product's base unit - the same unit the count sheet's
/// <c>system_qty</c> is printed in, so there is nothing to convert. Must not be negative.
/// </param>
/// <param name="CountedAt">When the count was taken. Null uses the current time.</param>
public sealed record RecordStockTakeCountCommand(
    long StockTakeId,
    long ProductVariantId,
    decimal CountedQty,
    DateTimeOffset? CountedAt = null);
