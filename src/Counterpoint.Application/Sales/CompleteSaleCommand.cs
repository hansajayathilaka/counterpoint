using System;
using System.Collections.Generic;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Complete the bill on the screen: allocate its number, persist it, move the stock, audit it
/// and queue the receipt - as one transaction (SRS FR-3.28, FR-3.30, SAD §7).
/// </summary>
/// <param name="UserId">The cashier ringing it up.</param>
/// <param name="ShiftId">The open shift it belongs to. A closed shift is refused by the database (AC-11).</param>
/// <param name="SoldAt">When the bill was completed. Its date is the trading day.</param>
/// <param name="Lines">What is being sold. At least one.</param>
/// <param name="Tenders">How it is being paid for. Must sum to the bill total.</param>
public sealed record CompleteSaleCommand(
    long UserId,
    long ShiftId,
    DateTimeOffset SoldAt,
    IReadOnlyList<SaleLineRequest> Lines,
    IReadOnlyList<TenderRequest> Tenders);
