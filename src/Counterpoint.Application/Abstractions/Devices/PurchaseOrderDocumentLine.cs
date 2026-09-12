using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>One printed line of a <see cref="PurchaseOrderDocument"/> (SRS FR-4.5).</summary>
public sealed record PurchaseOrderDocumentLine(
    string Sku,
    string Description,
    Quantity Qty,
    string UomSymbol,
    Money UnitCost,
    Money LineTotal);
