using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// A cancelled bill as the cancellation slip needs it (SRS FR-3.34: "reverses stock and prints a
/// cancellation slip").
/// </summary>
/// <param name="BillNo">The bill number - unchanged by the cancellation.</param>
/// <param name="SoldAt">When the original sale completed.</param>
/// <param name="CancelledAt">When the cancellation happened.</param>
/// <param name="Total">What the customer paid, and now does not.</param>
/// <param name="Reason">Why (SRS FR-3.34 requires one).</param>
/// <param name="CancelledBy">The owner who authorised it (SRS FR-1.6).</param>
public sealed record SaleCancellationReceipt(
    string BillNo,
    DateTimeOffset SoldAt,
    DateTimeOffset CancelledAt,
    Money Total,
    string Reason,
    string CancelledBy);
