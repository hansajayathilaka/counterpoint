using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of the purchase order list screen - header fields only, no lines (docs/01_DATA_MODEL.md §4).</summary>
public sealed record PurchaseOrderSummaryRecord(
    long Id,
    string PoNo,
    string SupplierName,
    DateTimeOffset OrderedAt,
    DateTimeOffset? ExpectedAt,
    string Status,
    int LineCount,
    Money Total);
