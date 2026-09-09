using Counterpoint.Application.Security;
using Counterpoint.Domain.Pricing;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Pricing;

/// <summary>
/// Authorises a line or bill discount against the shop's caps, the same "ask the owner" road
/// every other over-the-limit action takes (SRS FR-1.7, Q-12, task P1-T08 step 2).
/// </summary>
/// <remarks>
/// Not owner-only in its own right - a cashier applies a discount within the cap without needing
/// anyone's role. What is gated is going <em>over</em> the cap, and that gate is
/// <see cref="OverrideToken"/>, the same single-use, re-authenticated mechanism every owner
/// override uses (<c>IOwnerOverrideService</c>), not <see cref="Counterpoint.Application.Security.RequiresRoleAttribute"/>.
/// </remarks>
public interface IDiscountAuthorisationService
{
    /// <summary>
    /// Authorises a discount on one bill line, capped by the product's own
    /// <c>max_discount_rate</c> if it has one, otherwise by <c>policy.max_line_discount_rate</c>.
    /// </summary>
    /// <param name="discount">What the cashier asked for.</param>
    /// <param name="lineBaseAmount">The line's total before this discount.</param>
    /// <param name="productMaxDiscountRate"><c>product.max_discount_rate</c> for the item on this line, or null.</param>
    /// <param name="ownerOverride">
    /// A token from <c>IOwnerOverrideService.RequestAsync</c> for
    /// <see cref="PricingAuditActions.LineDiscountAboveLimit"/>, or null when none has been
    /// obtained. Only spent when the discount is actually above the cap.
    /// </param>
    /// <exception cref="DiscountLimitExceededException">
    /// The discount is above the cap and <paramref name="ownerOverride"/> is null, expired,
    /// already spent, or was granted for a different action.
    /// </exception>
    public DiscountEvaluation AuthoriseLineDiscount(
        DiscountInput discount,
        Money lineBaseAmount,
        Percentage? productMaxDiscountRate,
        OverrideToken? ownerOverride = null);

    /// <summary>
    /// Authorises a discount on the whole bill, capped by <c>policy.max_bill_discount_rate</c>.
    /// </summary>
    /// <param name="discount">What the cashier asked for.</param>
    /// <param name="billBaseAmount">The bill's subtotal before this discount.</param>
    /// <param name="ownerOverride">
    /// A token for <see cref="PricingAuditActions.BillDiscountAboveLimit"/>, as
    /// <see cref="AuthoriseLineDiscount"/>.
    /// </param>
    /// <exception cref="DiscountLimitExceededException">
    /// The discount is above the cap and <paramref name="ownerOverride"/> does not authorise it.
    /// </exception>
    public DiscountEvaluation AuthoriseBillDiscount(
        DiscountInput discount,
        Money billBaseAmount,
        OverrideToken? ownerOverride = null);
}
