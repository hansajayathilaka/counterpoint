using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// What the cashier is told once the bill is saved.
/// </summary>
/// <remarks>
/// No cost and no margin (CLAUDE.md invariant 8). No print outcome either: by the time this is
/// returned the receipt is a row in the outbox and the printer has not been touched, which is
/// exactly why a printer fault cannot fail a sale (AC-16).
/// </remarks>
/// <param name="SaleId">The bill's row id.</param>
/// <param name="BillNo">The allocated bill number, for example <c>INV-2026-000001</c>.</param>
/// <param name="Total">What the customer paid.</param>
/// <param name="PrintJobId">The queued receipt, so the UI can report on it later.</param>
public sealed record CompletedSale(long SaleId, string BillNo, Money Total, long PrintJobId);
