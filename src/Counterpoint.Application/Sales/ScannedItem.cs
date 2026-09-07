using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Sales;

/// <summary>
/// What the cashier's screen learns when a barcode is scanned.
/// </summary>
/// <remarks>
/// <b>There is no cost field, and that is the design.</b> Cost, margin and profit are excluded
/// at the query projection level for a cashier session, so there is nothing in the object that
/// reaches the UI to leak (CLAUDE.md invariant 8, SRS NFR-S2, AC-17). The cost that
/// <c>sale_line</c> snapshots is read separately, at completion, and never crosses this
/// boundary.
/// </remarks>
/// <param name="ProductVariantId">The variant to put on the bill.</param>
/// <param name="Description">The name to show and to snapshot.</param>
/// <param name="UomId">The unit the line is priced in.</param>
/// <param name="UomSymbol">That unit's symbol.</param>
/// <param name="UnitPrice">Price per unit.</param>
public sealed record ScannedItem(
    long ProductVariantId,
    string Description,
    long UomId,
    string UomSymbol,
    Money UnitPrice);
