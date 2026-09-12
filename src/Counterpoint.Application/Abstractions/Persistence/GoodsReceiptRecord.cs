using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One <c>goods_receipt</c> row with its lines and supplier name (docs/01_DATA_MODEL.md §4, SRS FR-4.7).</summary>
/// <param name="Id">The receipt's own id.</param>
/// <param name="GrnNo">The allocated document number.</param>
/// <param name="SupplierId">The supplier the stock was received from.</param>
/// <param name="SupplierName">The supplier's name, for display and for the printed document.</param>
/// <param name="PurchaseOrderId">The purchase order this receipt fulfils, or null.</param>
/// <param name="PurchaseOrderNo">That order's own document number, or null - for display only.</param>
/// <param name="SupplierInvoiceNo">The supplier's own invoice number.</param>
/// <param name="ReceivedAt">When the goods were received.</param>
/// <param name="Subtotal">The sum of every line's <c>qty × unit_cost</c>, before freight and tax.</param>
/// <param name="Tax">The sum of every line's tax.</param>
/// <param name="OtherCost">Freight and the like, apportioned across the lines.</param>
/// <param name="Total"><see cref="Subtotal"/> + <see cref="Tax"/> + <see cref="OtherCost"/>.</param>
/// <param name="UserId">The owner who recorded the receipt.</param>
/// <param name="Note">Free text, or null.</param>
/// <param name="Lines">Every line on the receipt.</param>
public sealed record GoodsReceiptRecord(
    long Id,
    string GrnNo,
    long SupplierId,
    string SupplierName,
    long? PurchaseOrderId,
    string? PurchaseOrderNo,
    string? SupplierInvoiceNo,
    DateTimeOffset ReceivedAt,
    Money Subtotal,
    Money Tax,
    Money OtherCost,
    Money Total,
    long UserId,
    string? Note,
    IReadOnlyList<GoodsReceiptLineRecord> Lines);
