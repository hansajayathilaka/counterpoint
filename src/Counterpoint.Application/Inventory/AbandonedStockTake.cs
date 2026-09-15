namespace Counterpoint.Application.Inventory;

/// <summary>What abandoning a stock take is told (task P2-T10).</summary>
/// <param name="StockTakeId">The stock take abandoned.</param>
/// <param name="StockTakeNo">Its document number, unchanged - the number stays on record even though nothing was posted.</param>
public sealed record AbandonedStockTake(long StockTakeId, string StockTakeNo);
