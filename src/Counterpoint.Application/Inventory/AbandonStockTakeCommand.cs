using System;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// Abandon a stock take (<see cref="IStockTakeService.AbandonAsync"/>, task P2-T10). Owner-only;
/// posts nothing.
/// </summary>
/// <param name="StockTakeId">The stock take to abandon. Must be <c>OPEN</c>.</param>
/// <param name="Reason">Why. Required, and audited - the same discipline <c>CancelSaleCommand</c> keeps.</param>
/// <param name="AbandonedAt">When. Null uses the current time.</param>
public sealed record AbandonStockTakeCommand(long StockTakeId, string Reason, DateTimeOffset? AbandonedAt = null);
