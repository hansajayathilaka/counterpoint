using System;
using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One <c>purchase_order</c> row with its lines and supplier name (docs/01_DATA_MODEL.md §4, SRS FR-4.5).</summary>
/// <param name="Id">The order's own id.</param>
/// <param name="PoNo">The allocated document number.</param>
/// <param name="SupplierId">The supplier the order is raised to.</param>
/// <param name="SupplierName">The supplier's name, for display and for the printed document.</param>
/// <param name="OrderedAt">When the order was raised.</param>
/// <param name="ExpectedAt">When the supplier is expected to deliver, or null.</param>
/// <param name="Status">A <c>Counterpoint.Domain.Purchasing.PurchaseOrderStatuses</c> token.</param>
/// <param name="UserId">The owner who raised it.</param>
/// <param name="Note">Free text, or null.</param>
/// <param name="Lines">Every line on the order.</param>
public sealed record PurchaseOrderRecord(
    long Id,
    string PoNo,
    long SupplierId,
    string SupplierName,
    DateTimeOffset OrderedAt,
    DateTimeOffset? ExpectedAt,
    string Status,
    long UserId,
    string? Note,
    IReadOnlyList<PurchaseOrderLineRecord> Lines)
{
    /// <summary>The order's total expected cost - the sum of every line's <see cref="PurchaseOrderLineRecord.LineTotal"/>.</summary>
    public Money Total => Money.FromScaled(Lines.Sum(line => line.LineTotal.ToScaled()));
}
