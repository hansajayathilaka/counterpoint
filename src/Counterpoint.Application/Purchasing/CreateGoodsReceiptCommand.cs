using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Purchasing;

/// <summary>One line of a goods receipt being recorded (SRS FR-4.7).</summary>
/// <param name="ProductVariantId">The variant received.</param>
/// <param name="UomId">The unit it was received in - any unit the product sells in, not necessarily its base unit (FR-2.4, FR-2.5).</param>
/// <param name="Quantity">How many, in <paramref name="UomId"/>.</param>
/// <param name="UnitCost">What the supplier charged per one of <paramref name="UomId"/>, as invoiced - no freight, no tax.</param>
/// <param name="Tax">Tax on this line, or null for none. <c>goods_receipt_line</c> carries no tax-class reference, so this is a plain entered amount, not computed from a rate.</param>
public sealed record CreateGoodsReceiptLineCommand(
    long ProductVariantId,
    long UomId,
    decimal Quantity,
    Money UnitCost,
    Money? Tax = null);

/// <summary>Records a new goods receipt, as <see cref="IGoodsReceiptService.ReceiveAsync"/> is asked (SRS FR-4.7).</summary>
/// <param name="SupplierId">The supplier the stock was received from.</param>
/// <param name="PurchaseOrderId">The purchase order this receipt fulfils, or null for a receipt with no preceding PO.</param>
/// <param name="SupplierInvoiceNo">The supplier's own invoice number, or null.</param>
/// <param name="ReceivedAt">When the goods were received, or null to use now.</param>
/// <param name="OtherCost">Freight and the like, apportioned across the lines by value (FR-4.7's "and other costs"). Zero when there is none.</param>
/// <param name="Note">Free text, or null.</param>
/// <param name="Lines">Every line on the receipt. Never empty.</param>
public sealed record CreateGoodsReceiptCommand(
    long SupplierId,
    long? PurchaseOrderId,
    string? SupplierInvoiceNo,
    DateTimeOffset? ReceivedAt,
    Money OtherCost,
    string? Note,
    IReadOnlyList<CreateGoodsReceiptLineCommand> Lines);
