using System;
using Counterpoint.Application.Security;
using Counterpoint.Domain.Returns;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Returns;

/// <summary>
/// Authorises a return against the shop's policy settings, the same "ask the owner" road every
/// other over-the-limit action takes (SRS FR-5, BR-*, FR-10.5, Q-03, task P2-T01).
/// </summary>
/// <remarks>
/// Not owner-only in its own right - a cashier processes an ordinary, in-window, receipted
/// return without needing anyone's role. What is gated is the exception to that, and that gate is
/// <see cref="OverrideToken"/>, the same single-use, re-authenticated mechanism every owner
/// override uses (<see cref="IOwnerOverrideService"/>), not
/// <see cref="Counterpoint.Application.Security.RequiresRoleAttribute"/>.
/// </remarks>
public interface IReturnPolicyAuthorisationService
{
    /// <summary>
    /// Authorises a return against the configured return window (SRS FR-5.6), reading
    /// <c>policy.return_window_days</c> from settings on every call.
    /// </summary>
    /// <param name="saleDate">When the original sale completed.</param>
    /// <param name="ownerOverride">
    /// A token from <see cref="IOwnerOverrideService.RequestAsync"/> for
    /// <see cref="ReturnPolicyAuditActions.ReturnWindowExceeded"/>, or null when none has been
    /// obtained. Only spent when the return is actually outside the window.
    /// </param>
    /// <exception cref="ReturnNotEligibleException">
    /// The window has passed and <paramref name="ownerOverride"/> is null, expired, already
    /// spent, or was granted for a different action.
    /// </exception>
    public ReturnEligibility AuthoriseReturnWindow(DateTimeOffset saleDate, OverrideToken? ownerOverride = null);

    /// <summary>
    /// Authorises a return against the per-product <c>non_returnable</c> flag and the shop's
    /// non-returnable category list (SRS FR-5.10, AC-05).
    /// </summary>
    /// <param name="productNonReturnable"><c>product.non_returnable</c> for the item on this line.</param>
    /// <param name="categoryId">The item's <c>category_id</c>, or null if it has none.</param>
    /// <param name="ownerOverride">
    /// A token for <see cref="ReturnPolicyAuditActions.NonReturnableOverride"/>, as
    /// <see cref="AuthoriseReturnWindow"/>.
    /// </param>
    /// <exception cref="ReturnNotEligibleException">
    /// The item or its category is non-returnable and <paramref name="ownerOverride"/> does not
    /// authorise it.
    /// </exception>
    public ReturnEligibility AuthoriseNonReturnable(
        bool productNonReturnable, long? categoryId, OverrideToken? ownerOverride = null);

    /// <summary>
    /// Authorises the quantity being returned against the cumulative history of every return
    /// already taken against this bill line (SRS BR-05, AC-06).
    /// </summary>
    /// <param name="soldQuantity"><c>sale_line.qty</c>.</param>
    /// <param name="alreadyReturnedQuantity"><c>sale_line.qty_returned</c> before this attempt.</param>
    /// <param name="requestedQuantity">What the cashier is trying to return right now.</param>
    /// <remarks>
    /// Deliberately takes no <see cref="OverrideToken"/> parameter at all. There is no way to
    /// call this method with permission to exceed the cumulative limit - see the risk note on
    /// task P2-T01 and the remarks on <see cref="ReturnEligibility"/>.
    /// </remarks>
    /// <exception cref="ReturnNotEligibleException">
    /// The requested quantity, added to what has already been returned, would exceed what was
    /// sold. Never overridable.
    /// </exception>
    public ReturnEligibility AuthoriseCumulativeQuantity(
        Quantity soldQuantity, Quantity alreadyReturnedQuantity, Quantity requestedQuantity);

    /// <summary>
    /// Authorises a return with no original bill (SRS FR-5.19, Q-03), reading
    /// <c>policy.allow_unlinked_returns</c> from settings on every call.
    /// </summary>
    /// <param name="ownerOverride">
    /// A token for <see cref="ReturnPolicyAuditActions.UnlinkedReturn"/>. Even with the feature
    /// enabled in settings, every individual unlinked return still needs one (task P2-T03 step 2)
    /// - there is no path through this method that skips it.
    /// </param>
    /// <exception cref="ReturnNotEligibleException">
    /// Unlinked returns are disabled in settings (never overridable - change the setting
    /// instead), or they are enabled but <paramref name="ownerOverride"/> does not authorise this
    /// one.
    /// </exception>
    public ReturnEligibility AuthoriseUnlinkedReturn(OverrideToken? ownerOverride = null);

    /// <summary>
    /// Authorises a return whose bill was found without its own number being scanned or typed
    /// (SRS FR-5.1, Q-03), reading <c>policy.receipt_required</c> from settings on every call.
    /// </summary>
    /// <param name="billReferencePresented">
    /// The bill was identified by its own number rather than only found by a date/customer/amount
    /// search.
    /// </param>
    /// <param name="ownerOverride">
    /// A token for <see cref="ReturnPolicyAuditActions.ReceiptNotPresented"/>, as
    /// <see cref="AuthoriseReturnWindow"/>.
    /// </param>
    /// <exception cref="ReturnNotEligibleException">
    /// A receipt is required, none was presented, and <paramref name="ownerOverride"/> does not
    /// authorise it.
    /// </exception>
    public ReturnEligibility AuthoriseReceiptRequirement(
        bool billReferencePresented, OverrideToken? ownerOverride = null);

    /// <summary>
    /// Authorises a cash refund against the configured limit (SRS FR-5.13), reading
    /// <c>policy.cash_refund_limit</c> from settings on every call.
    /// </summary>
    /// <param name="isCashRefund">The refund method chosen for this return is cash.</param>
    /// <param name="refundAmount">The refund amount, as a non-negative magnitude.</param>
    /// <param name="ownerOverride">
    /// A token for <see cref="ReturnPolicyAuditActions.CashRefundLimitExceeded"/>, as
    /// <see cref="AuthoriseReturnWindow"/>.
    /// </param>
    /// <exception cref="ReturnNotEligibleException">
    /// The cash refund is above the limit and <paramref name="ownerOverride"/> does not authorise
    /// it.
    /// </exception>
    public ReturnEligibility AuthoriseCashRefundLimit(
        bool isCashRefund, Money refundAmount, OverrideToken? ownerOverride = null);
}
