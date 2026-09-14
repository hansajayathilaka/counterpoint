using System;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// Post a stock take's corrections as one batch (<see cref="IStockTakeService.PostAsync"/>,
/// task P2-T10). Owner-only.
/// </summary>
/// <param name="StockTakeId">The stock take to post. Must be <c>OPEN</c>.</param>
/// <param name="PostedAt">When the batch is posted. Null uses the current time.</param>
public sealed record PostStockTakeCommand(long StockTakeId, DateTimeOffset? PostedAt = null);
