using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>One printed line of a <see cref="GoodsReceiptDocument"/> (SRS FR-4.7, FR-7.10).</summary>
public sealed record GoodsReceiptDocumentLine(
    string Sku,
    string Description,
    Quantity Qty,
    string UomSymbol,
    Money UnitCost,
    Money Tax,
    Money LineTotal);
