namespace Counterpoint.Application.Sales;

/// <summary>What the owner is told once a bill is cancelled (SRS FR-3.34).</summary>
/// <param name="SaleId">The bill's row id - unchanged.</param>
/// <param name="BillNo">The bill number - unchanged (CLAUDE.md invariant 4).</param>
/// <param name="ReversedMovementCount">
/// How many <c>stock_movement</c> rows were posted to reverse it - zero for a bill of open items
/// and services alone.
/// </param>
/// <param name="PrintJobId">The queued cancellation slip, so the UI can report on it later.</param>
public sealed record CancelledSale(long SaleId, string BillNo, int ReversedMovementCount, long PrintJobId);
