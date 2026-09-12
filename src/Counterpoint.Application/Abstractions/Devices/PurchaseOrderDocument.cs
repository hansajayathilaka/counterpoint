using System;
using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// A purchase order, in the shape <see cref="IPurchaseOrderDocumentRenderer"/> prints it (SRS
/// FR-4.5: "PO must be printable/exportable").
/// </summary>
public sealed record PurchaseOrderDocument(
    string PoNo,
    DateTimeOffset OrderedAt,
    DateTimeOffset? ExpectedAt,
    string SupplierName,
    string? SupplierAddress,
    string? SupplierPhone,
    string Status,
    string RaisedByName,
    string? Note,
    IReadOnlyList<PurchaseOrderDocumentLine> Lines)
{
    /// <summary>The order's total expected cost - the sum of every line's <see cref="PurchaseOrderDocumentLine.LineTotal"/>.</summary>
    public Money Total => Money.FromScaled(Lines.Sum(line => line.LineTotal.ToScaled()));
}
