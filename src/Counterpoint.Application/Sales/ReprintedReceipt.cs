namespace Counterpoint.Application.Sales;

/// <summary>What a successful reprint produced.</summary>
/// <param name="SaleId">The bill's row id.</param>
/// <param name="BillNo">The bill number, unchanged (CLAUDE.md invariant 4).</param>
/// <param name="PrintJobId">The new outbox row queued for this reprint.</param>
public sealed record ReprintedReceipt(long SaleId, string BillNo, long PrintJobId);
