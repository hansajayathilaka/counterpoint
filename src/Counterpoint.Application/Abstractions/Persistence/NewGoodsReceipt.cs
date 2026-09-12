using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// A new <c>goods_receipt</c> row and its lines, written together in one insert
/// (docs/01_DATA_MODEL.md §4, SRS FR-4.7).
/// </summary>
/// <param name="GrnNo">Allocated from <c>number_sequence</c> before this reaches the store (CLAUDE.md invariant 4).</param>
/// <param name="SupplierId">The supplier the stock was received from.</param>
/// <param name="PurchaseOrderId">The purchase order this receipt fulfils, or null for a receipt with no preceding PO (FR-4.7).</param>
/// <param name="SupplierInvoiceNo">The supplier's own invoice number, as printed on their paperwork.</param>
/// <param name="ReceivedAt">When the goods were received.</param>
/// <param name="Subtotal">The sum of every line's <c>qty × unit_cost</c>, before freight and tax.</param>
/// <param name="Tax">The sum of every line's tax.</param>
/// <param name="OtherCost">Freight and the like, apportioned across the lines (SRS FR-4.7's "and other costs").</param>
/// <param name="Total"><see cref="Subtotal"/> + <see cref="Tax"/> + <see cref="OtherCost"/>, exactly - the sum of every line's <c>line_total</c> too.</param>
/// <param name="UserId">The owner who recorded the receipt.</param>
/// <param name="Note">Free text, or null.</param>
/// <param name="Lines">Every line on the receipt. Never empty.</param>
public sealed record NewGoodsReceipt(
    string GrnNo,
    long SupplierId,
    long? PurchaseOrderId,
    string? SupplierInvoiceNo,
    DateTimeOffset ReceivedAt,
    Money Subtotal,
    Money Tax,
    Money OtherCost,
    Money Total,
    long UserId,
    string? Note,
    IReadOnlyList<NewGoodsReceiptLine> Lines);
