using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of a goods-receipt list screen - header fields only, no lines (docs/01_DATA_MODEL.md §4).</summary>
public sealed record GoodsReceiptSummaryRecord(
    long Id,
    string GrnNo,
    string SupplierName,
    long? PurchaseOrderId,
    string? PurchaseOrderNo,
    DateTimeOffset ReceivedAt,
    Money Total,
    int LineCount);
