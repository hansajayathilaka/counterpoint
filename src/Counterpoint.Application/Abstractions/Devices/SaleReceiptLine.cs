using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>One printed bill line.</summary>
/// <param name="Description">The name as sold.</param>
/// <param name="Quantity">How much, in the unit it was sold in.</param>
/// <param name="UomSymbol">That unit's symbol, for example <c>pc</c>.</param>
/// <param name="UnitPrice">Price per unit, as charged.</param>
/// <param name="LineTotal">The rounded line total.</param>
public sealed record SaleReceiptLine(
    string Description,
    Quantity Quantity,
    string UomSymbol,
    Money UnitPrice,
    Money LineTotal);
