using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The owner's bulk-break door (SRS FR-4.9, AC-09, task P2-T09): converts stock from one
/// packaging form into another - a coil into loose metres, a box into loose pieces - moving value
/// between two different <c>product_variant</c> rows while conserving it.
/// </summary>
/// <remarks>
/// <para>
/// Owner-only. FR-4.9 reads "the owner must be able to convert stock from one form to another" -
/// the same phrasing FR-4.5 (purchase orders), FR-4.7 (GRN) and FR-4.12 (adjustments) use for the
/// doors this codebase already gates to <see cref="Role.Owner"/>
/// (<c>Counterpoint.Application.Purchasing.IGoodsReceiptService</c>,
/// <see cref="Counterpoint.Application.Inventory.IPostAdjustment"/>). A bulk break is, mechanically,
/// exactly the same shrinkage-risk shape task P2-T08 already reasoned through for adjustments and
/// damage: an unchecked stock change with no receipt or sale behind it, valued by whoever posts it.
/// Wired identically - the composition root wraps the concrete handler in
/// <c>RoleAuthorisation.Decorate</c> and only ever hands out this interface, so a service-level
/// test that calls it directly, signed in as a cashier, is what proves AC-17 for this door with no
/// UI in sight.
/// </para>
/// <para>
/// Every call posts through <c>IStockLedger.PostAsync</c> (CLAUDE.md invariant 3) - a
/// <c>BULK_BREAK_OUT</c> on the source, a <c>BULK_BREAK_IN</c> on the destination, and, when
/// wastage was declared, a <c>DAMAGE</c> write-off - never a direct write to the ledger or its
/// balance projection.
/// </para>
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IPostBulkBreak
{
    /// <summary>
    /// Posts one bulk break: a balanced <c>BULK_BREAK_OUT</c>/<c>BULK_BREAK_IN</c> pair sharing
    /// one <c>ref_doc_id</c>, and, when the actual yield fell short of what was expected, a
    /// <c>DAMAGE</c> write-off for the difference - all three in one transaction, together with
    /// the <c>bulk_break</c> header row that mints the id they share.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The reason is blank, either variant does not exist or carries no stock, the source and
    /// destination are the same variant, a quantity is not positive, or the actual quantity
    /// exceeds the expected quantity.
    /// </exception>
    public Task<BulkBreakResult> PostAsync(
        BulkBreakCommand command, CancellationToken cancellationToken = default);
}
