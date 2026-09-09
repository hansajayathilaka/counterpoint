using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One <c>stock_movement</c> row a completed sale posted, read back so a cancellation can reverse
/// it exactly (SRS FR-3.34, CLAUDE.md invariant 3).
/// </summary>
/// <param name="ProductVariantId">The variant that moved.</param>
/// <param name="QuantityBase">
/// The <em>original</em> movement's magnitude, always positive - what the sale took out, and so
/// what the cancellation puts back. <c>Counterpoint.Application.Sales.CancelSaleHandler</c> posts
/// this unchanged as an inbound movement; it never re-derives it from the catalogue or the
/// product's current type, both of which can have moved on since the sale.
/// </param>
/// <param name="UnitCost">
/// The original movement's own <c>unit_cost</c> - the COGS snapshot the sale took. Posting the
/// reversal back in at this same cost is what restores the moving-average cost to what it was
/// immediately before the sale (SRS FR-4, DM-05): a symmetric pair around one average leaves it
/// unchanged, to the scaled integer.
/// </param>
public sealed record SaleStockReversal(long ProductVariantId, Quantity QuantityBase, Money UnitCost);

/// <summary>
/// A completed sale, read back with exactly what a cancellation needs to check and reverse it
/// (SRS FR-3.34).
/// </summary>
/// <param name="Id">The sale's row id.</param>
/// <param name="BillNo">The bill number - unchanged by a cancellation (CLAUDE.md invariant 4).</param>
/// <param name="Status">
/// <c>COMPLETED</c> or <c>CANCELLED</c>, as it stands right now. A sale already cancelled is
/// refused by the Application layer before the database's own one-way trigger would refuse it,
/// so the cashier sees a plain sentence instead of a raw constraint violation.
/// </param>
/// <param name="BusinessDate">
/// The trading day the sale belongs to. FR-3.34 permits cancellation only on the same business
/// day.
/// </param>
/// <param name="SoldAt">When the original sale completed - printed on the cancellation slip.</param>
/// <param name="Total">What the customer paid - printed on the cancellation slip.</param>
/// <param name="StockMovements">
/// Every <c>stock_movement</c> row the sale posted (<c>ref_doc_type = 'SALE'</c>,
/// <c>ref_doc_id</c> = this sale). Empty for a bill of open items and services alone.
/// </param>
public sealed record SaleForCancellation(
    long Id,
    string BillNo,
    string Status,
    DateOnly BusinessDate,
    DateTimeOffset SoldAt,
    Money Total,
    IReadOnlyList<SaleStockReversal> StockMovements);
