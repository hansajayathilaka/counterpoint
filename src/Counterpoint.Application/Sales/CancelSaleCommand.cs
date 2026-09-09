using System;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Cancel a completed bill (SRS FR-3.34): owner-only, same business day only, a reason is
/// required, stock is reversed, and the bill keeps its number.
/// </summary>
/// <param name="SaleId">The bill to cancel.</param>
/// <param name="Reason">Why. Required - refused when blank (SRS FR-3.34, FR-1.6).</param>
/// <param name="CancelledAt">
/// When the cancellation happens. Its business date must match the sale's own, or it is refused
/// (SRS FR-3.34: "same business day only").
/// </param>
public sealed record CancelSaleCommand(long SaleId, string Reason, DateTimeOffset CancelledAt);
