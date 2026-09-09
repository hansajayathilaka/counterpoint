using System;
using System.Collections.Generic;
using Counterpoint.Domain.Pricing;

namespace Counterpoint.Application.Sales;

/// <summary>
/// Complete the bill on the screen: allocate its number, persist it, move the stock, audit it
/// and queue the receipt - as one transaction (SRS FR-3.28, FR-3.30, SAD §7).
/// </summary>
/// <param name="UserId">The cashier ringing it up.</param>
/// <param name="ShiftId">The open shift it belongs to. A closed shift is refused by the database (AC-11).</param>
/// <param name="SoldAt">When the bill was completed. Its date is the trading day.</param>
/// <param name="Lines">What is being sold. At least one.</param>
/// <param name="Tenders">
/// How it is being paid for - what was actually offered, in the order it was offered. A cash
/// tender may be more than the bill total; the excess comes back as
/// <see cref="CompletedSale.Change"/>, never as a second payment row (SRS FR-3.26). Every other
/// tender type must not exceed what the bill still owes when it is taken
/// (<c>Counterpoint.Domain.Sales.TenderCalculator</c>).
/// </param>
/// <param name="CustomerId">
/// The customer to attach, or null for the default anonymous walk-in sale (SRS FR-3.21, FR-3.22).
/// Attaching a customer does not change pricing in this phase - trade price tiers are Phase 5
/// (docs/03_PHASE_1_core_trading.md, "out of scope in this phase").
/// </param>
/// <param name="BillDiscount">A whole-bill discount, as <see cref="IQuoteSale.QuoteAsync"/> priced it (SRS FR-3.17).</param>
public sealed record CompleteSaleCommand(
    long UserId,
    long ShiftId,
    DateTimeOffset SoldAt,
    IReadOnlyList<SaleLineRequest> Lines,
    IReadOnlyList<TenderRequest> Tenders,
    long? CustomerId = null,
    DiscountInput? BillDiscount = null);
