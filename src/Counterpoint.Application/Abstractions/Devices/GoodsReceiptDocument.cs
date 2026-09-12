using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// A goods receipt note, in the shape <see cref="IGoodsReceiptDocumentRenderer"/> prints it (SRS
/// FR-4.7, FR-7.10: "the system must print ... GRN").
/// </summary>
public sealed record GoodsReceiptDocument(
    string GrnNo,
    DateTimeOffset ReceivedAt,
    string SupplierName,
    string? SupplierInvoiceNo,
    string? PurchaseOrderNo,
    string ReceivedByName,
    string? Note,
    Money Subtotal,
    Money Tax,
    Money OtherCost,
    Money Total,
    IReadOnlyList<GoodsReceiptDocumentLine> Lines);
