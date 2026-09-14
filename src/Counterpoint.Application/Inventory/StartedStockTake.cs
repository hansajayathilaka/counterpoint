namespace Counterpoint.Application.Inventory;

/// <summary>What starting a stock take is told (SRS FR-4 stock take, task P2-T10).</summary>
/// <param name="StockTakeId">The new <c>stock_take.id</c>.</param>
/// <param name="StockTakeNo">The allocated document number, printed on the count sheet (FR-7.10).</param>
/// <param name="LineCount">How many variants the scope resolved to - the count sheet's own line count.</param>
public sealed record StartedStockTake(long StockTakeId, string StockTakeNo, int LineCount);
