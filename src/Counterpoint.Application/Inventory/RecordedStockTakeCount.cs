using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Inventory;

/// <summary>What recording a count is told (task P2-T10).</summary>
/// <param name="StockTakeId">The stock take the line belongs to.</param>
/// <param name="ProductVariantId">The variant counted.</param>
/// <param name="SystemQty">What was frozen at generation - unchanged by this call.</param>
/// <param name="CountedQty">What was just recorded.</param>
/// <param name="Variance"><c>CountedQty - SystemQty</c>.</param>
public sealed record RecordedStockTakeCount(
    long StockTakeId,
    long ProductVariantId,
    Quantity SystemQty,
    Quantity CountedQty,
    Quantity Variance);
