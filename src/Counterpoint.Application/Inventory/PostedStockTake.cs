namespace Counterpoint.Application.Inventory;

/// <summary>What posting a stock take is told (SRS AC-10, task P2-T10).</summary>
/// <param name="StockTakeId">The stock take posted.</param>
/// <param name="StockTakeNo">Its document number, unchanged.</param>
/// <param name="MovementsPosted">
/// How many <c>STOCK_TAKE</c> movements were posted - one per counted line whose variance was not
/// zero.
/// </param>
/// <param name="LinesSkipped">
/// How many lines were left alone: never counted, or counted with no variance from the frozen
/// figure.
/// </param>
public sealed record PostedStockTake(long StockTakeId, string StockTakeNo, int MovementsPosted, int LinesSkipped);
