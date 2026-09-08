using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Inventory;

/// <summary>
/// The stock enquiry screen's use case (F11, P1-T07, SRS FR-4).
/// </summary>
/// <remarks>
/// Available to a cashier session as well as an owner's - "check stock" is explicitly a cashier
/// capability (<c>Counterpoint.Domain.Security.Role</c>) - so this carries no
/// <c>RequiresRoleAttribute</c>. What differs by role is one field inside the result, not whether
/// the call is allowed at all: cost is stripped at the projection level for a cashier session
/// (CLAUDE.md invariant 8), never by the caller deciding not to look at it.
/// </remarks>
public interface IStockEnquiry
{
    /// <summary>
    /// The current stock position for one variant: quantity in base and alternate units, cost
    /// for an owner session, and its most recent movements. Null when the variant does not exist
    /// or is not sellable.
    /// </summary>
    public Task<StockEnquiryResult?> FindByVariantIdAsync(
        long productVariantId,
        CancellationToken cancellationToken = default);
}
