using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// One line of a bill as priced by the Application layer, ready for the screen.
/// </summary>
/// <remarks>
/// No cost, no margin (CLAUDE.md invariant 8). <see cref="LineTotal"/> is the rounded figure
/// that will be charged, not an estimate the UI works out for itself - pricing arithmetic never
/// happens twice, and never above the Application layer.
/// </remarks>
/// <param name="ProductVariantId">The variant on the line, or null for an open item (SRS FR-2.8).</param>
/// <param name="Description">The name as it will be snapshotted onto the bill.</param>
/// <param name="Quantity">How much, in the unit it is priced in.</param>
/// <param name="UomSymbol">That unit's symbol.</param>
/// <param name="UnitPrice">Price per unit.</param>
/// <param name="Discount">The discount taken off this line (SRS FR-3.4, FR-3.16). Zero when none was asked for.</param>
/// <param name="LineTotal">The rounded line total, net of <see cref="Discount"/>.</param>
/// <param name="IsOpenItem">
/// True for a manually described, manually priced line (SRS FR-2.8) - flagged so a report can
/// single these out, exactly as FR-2.8 requires.
/// </param>
public sealed record QuotedLine(
    long? ProductVariantId,
    string Description,
    decimal Quantity,
    string UomSymbol,
    Money UnitPrice,
    Money Discount,
    Money LineTotal,
    bool IsOpenItem);
