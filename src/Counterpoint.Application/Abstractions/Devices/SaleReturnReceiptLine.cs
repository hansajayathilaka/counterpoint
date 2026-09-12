using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>One printed return line (task P2-T02).</summary>
/// <param name="Description">The name as originally sold.</param>
/// <param name="Quantity">How much came back, in base units.</param>
/// <param name="UomSymbol">The unit it was originally sold in, for display.</param>
/// <param name="UnitPrice">The price ORIGINALLY paid (SRS AC-03) - never today's price.</param>
/// <param name="LineRefund">What this line refunds, before the restocking fee.</param>
/// <param name="Disposition">
/// <c>SELLABLE</c> or <c>DAMAGED</c> (SRS FR-5.8) - printed so the customer can see which of
/// their returned items went back on the shelf.
/// </param>
public sealed record SaleReturnReceiptLine(
    string Description,
    Quantity Quantity,
    string UomSymbol,
    Money UnitPrice,
    Money LineRefund,
    string Disposition);
