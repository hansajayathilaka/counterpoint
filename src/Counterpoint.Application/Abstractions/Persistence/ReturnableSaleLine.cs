using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// One line of a completed bill, as a return needs to see it: what was sold, what has already
/// come back, and what is still available to return (task P2-T02, SRS FR-5.1-FR-5.10).
/// </summary>
/// <remarks>
/// <see cref="UnitPrice"/> and <see cref="UnitCost"/> are the snapshots <c>sale_line</c> already
/// carries (CLAUDE.md invariant 10) - never a fresh catalogue lookup. This type carries no
/// current selling price at all, which is what makes refunding at today's price structurally
/// impossible from here: there is nothing on this record to refund at except what was paid.
/// </remarks>
/// <param name="SaleLineId">The original bill line this return line reverses.</param>
/// <param name="ProductVariantId">The variant sold, or null for an open item (which posts no stock either way).</param>
/// <param name="Description">The name as sold, snapshotted on the original line.</param>
/// <param name="UomSymbol">The unit the line was sold in, for display only.</param>
/// <param name="QtySoldBase">
/// <c>sale_line.qty_base</c>, tagged with <paramref name="ProductVariantId"/> the same
/// bookkeeping way <c>SqliteSaleLookup</c> tags a reversal quantity - stock and returns both work
/// in base units, and <c>sale_return_line</c> itself carries no <c>uom_id</c> column to convert
/// back from.
/// </param>
/// <param name="QtyReturnedBase"><c>sale_line.qty_returned</c> before this attempt (AC-06).</param>
/// <param name="UnitPrice">The price ORIGINALLY paid, per <c>sale_line.uom_id</c> (AC-03).</param>
/// <param name="UnitCost">Cost per base unit at the moment of sale (CLAUDE.md invariant 10).</param>
/// <param name="LineTotal">The line's rounded net total as sold (post discount, pre tax).</param>
/// <param name="Tax">Tax on the line as sold.</param>
/// <param name="NonReturnable"><c>product.non_returnable</c> for the item on this line (SRS FR-5.10, AC-05).</param>
/// <param name="CategoryId">The item's <c>category_id</c>, or null - the other half of FR-5.10's check.</param>
public sealed record ReturnableSaleLine(
    long SaleLineId,
    long? ProductVariantId,
    string Description,
    string UomSymbol,
    Quantity QtySoldBase,
    Quantity QtyReturnedBase,
    Money UnitPrice,
    Money UnitCost,
    Money LineTotal,
    Money Tax,
    bool NonReturnable,
    long? CategoryId)
{
    /// <summary>What is still returnable on this line - <see cref="QtySoldBase"/> less what has already come back.</summary>
    public Quantity QtyAvailableBase => QtySoldBase - QtyReturnedBase;
}
