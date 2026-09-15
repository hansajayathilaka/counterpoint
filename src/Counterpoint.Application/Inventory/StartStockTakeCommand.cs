using System;

namespace Counterpoint.Application.Inventory;

/// <summary>Generate a count sheet (<see cref="IStockTakeService.StartAsync"/>, task P2-T10).</summary>
/// <param name="Scope">
/// Operator input, not yet validated: <c>ALL</c>, <c>CATEGORY:&lt;id&gt;</c>,
/// <c>BRAND:&lt;id&gt;</c> or <c>LOCATION:&lt;rack&gt;</c>. Parsed and canonicalised by
/// <see cref="Domain.Inventory.StockTakeScope.Parse"/> before anything is written.
/// </param>
/// <param name="StartedAt">
/// When the count sheet is generated. Null uses the current time - a test supplies one to make
/// the freeze instant deterministic.
/// </param>
public sealed record StartStockTakeCommand(string Scope, DateTimeOffset? StartedAt = null);
