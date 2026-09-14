using System.Threading;
using System.Threading.Tasks;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The owner's manual stock-change door (SRS FR-4, NFR-S2, task P2-T08) - the one way a variant's
/// stock is corrected or written off outside a document (a GRN, a sale, a return) that already
/// carries its own quantity. Every call still posts through <c>IStockLedger.PostAsync</c>
/// (CLAUDE.md invariant 3): this is not a second door onto stock, it is the owner-only front door
/// of the same one - a cashier has no other way to move a balance without a document behind it.
/// </summary>
/// <remarks>
/// Owner-only for the reason task P2-T08's own "Context" gives: an unchecked stock change is
/// this shop's own definition of the shrinkage risk. Wired exactly as
/// <see cref="Counterpoint.Application.Sales.ICancelSale"/> is - the composition root wraps the
/// concrete handler in <c>RoleAuthorisation.Decorate</c> and only ever hands out this interface,
/// so a service-level test that calls it directly, signed in as a cashier, is what proves AC-17
/// for this door with no UI in sight.
/// </remarks>
[RequiresRole(Role.Owner)]
public interface IPostAdjustment
{
    /// <summary>
    /// Corrects a variant's stock by a signed quantity, or to a target quantity - never both, and
    /// never neither (see <see cref="AdjustmentCommand"/>'s own remarks). Posts an
    /// <c>ADJUSTMENT</c> movement, valued at the variant's own moving-average cost read at the
    /// moment of posting, so a count correction can never perturb the average the way a real
    /// purchase would.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The reason is blank, neither or both of the two quantity modes were given, the resulting
    /// change is zero, the variant does not exist, or the product carries no stock to adjust.
    /// </exception>
    public Task<AdjustmentResult> AdjustAsync(
        AdjustmentCommand command, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes off a quantity as damaged: always a stock decrease, posted as a <c>DAMAGE</c>
    /// movement, at the variant's own moving-average cost - the value the shop is actually
    /// losing, not a guess.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">
    /// The reason is blank, the quantity is not positive, the variant does not exist, or the
    /// product carries no stock to write off.
    /// </exception>
    public Task<AdjustmentResult> WriteOffDamageAsync(
        DamageCommand command, CancellationToken cancellationToken = default);
}
