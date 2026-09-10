using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Returns;

/// <summary>
/// The return rules that decide whether a line may go back, evaluated from settings already
/// resolved by the caller (SRS FR-5, BR-05, BR-06, FR-10.5, Q-03, task P2-T01 step 1).
/// </summary>
/// <remarks>
/// <para>
/// Pure evaluation only, the same split as <c>Counterpoint.Domain.Pricing.DiscountCapPolicy</c>:
/// this class never reads <c>ISettings</c>, never touches an <c>OverrideToken</c> and never
/// writes an audit row. <c>Counterpoint.Application.Returns.IReturnPolicyAuthorisationService</c>
/// is the half of this that actually stops a return without a granted override; this is the rule
/// that decision is based on, kept separate so each rule is provable without a database, a
/// session or a settings framework in the way.
/// </para>
/// <para>
/// Every method here is independent - one rule, one question, one <see cref="ReturnEligibility"/>.
/// Combining several rules into the one answer a return screen shows for a line is the caller's
/// job (task P2-T02), because only the caller knows which rules even apply to the return being
/// attempted (a cash-refund check has nothing to say about a store-credit refund, for instance).
/// </para>
/// </remarks>
public static class ReturnPolicy
{
    /// <summary>
    /// SRS FR-5.6: a return outside the configured window is blocked, or allowed only with an
    /// owner override and a recorded reason - so this never denies outright, it only ever asks
    /// for permission once the window has passed.
    /// </summary>
    /// <param name="saleDate">When the original sale completed.</param>
    /// <param name="now">The moment the return is being attempted.</param>
    /// <param name="windowDays"><c>policy.return_window_days</c> (SRS FR-10.5, Q-03: 14 by default).</param>
    public static ReturnEligibility EvaluateReturnWindow(DateTimeOffset saleDate, DateTimeOffset now, int windowDays)
    {
        if (windowDays < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowDays), windowDays, "A return window cannot be a negative number of days.");
        }

        var deadline = saleDate.AddDays(windowDays);

        return now <= deadline
            ? ReturnEligibility.Allowed.Instance
            : new ReturnEligibility.AllowedWithOverride(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"The {windowDays}-day return window closed on {deadline:yyyy-MM-dd}. An owner has to authorise this return."));
    }

    /// <summary>
    /// SRS FR-5.10, AC-05: a product flagged non-returnable, or one whose category the shop has
    /// marked non-returnable, may only be returned with an owner override - never outright, and
    /// never denied without one either, matching FR-5.10's own wording ("overridable by the owner
    /// only").
    /// </summary>
    /// <param name="productNonReturnable"><c>product.non_returnable</c> for the item on this line.</param>
    /// <param name="categoryNonReturnable">
    /// Whether the product's <c>category_id</c> is one of <c>policy.non_returnable_category_ids</c>.
    /// </param>
    public static ReturnEligibility EvaluateNonReturnable(bool productNonReturnable, bool categoryNonReturnable)
    {
        if (!productNonReturnable && !categoryNonReturnable)
        {
            return ReturnEligibility.Allowed.Instance;
        }

        var reason = productNonReturnable
            ? "This item is flagged non-returnable (final sale). An owner has to authorise this return."
            : "This item's category is non-returnable. An owner has to authorise this return.";

        return new ReturnEligibility.AllowedWithOverride(reason);
    }

    /// <summary>
    /// SRS BR-05, AC-06: cumulative returns against a bill line may never exceed the quantity
    /// sold on it. This is the one rule in the file that only ever returns
    /// <see cref="ReturnEligibility.Allowed"/> or <see cref="ReturnEligibility.Denied"/> - there
    /// is no <see cref="ReturnEligibility.AllowedWithOverride"/> arm here, on purpose, and there
    /// must never be one added: over-return is a fraud control, not a policy preference (task
    /// P2-T01 risk note).
    /// </summary>
    /// <param name="soldQuantity"><c>sale_line.qty</c> - what was sold on the original line.</param>
    /// <param name="alreadyReturnedQuantity"><c>sale_line.qty_returned</c> before this attempt.</param>
    /// <param name="requestedQuantity">What the cashier is trying to return right now.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="requestedQuantity"/> is zero or negative - a return has to ask for
    /// something.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// The three quantities are not all in the same unit of measure.
    /// </exception>
    public static ReturnEligibility EvaluateCumulativeQuantity(
        Quantity soldQuantity, Quantity alreadyReturnedQuantity, Quantity requestedQuantity)
    {
        if (!requestedQuantity.IsPositive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedQuantity),
                requestedQuantity.Value,
                "A return has to ask for a positive quantity.");
        }

        var available = soldQuantity - alreadyReturnedQuantity;

        if (requestedQuantity > available)
        {
            return new ReturnEligibility.Denied(available.IsPositive
                ? $"Only {available.Value} of {soldQuantity.Value} sold is still returnable on this line; "
                  + $"{requestedQuantity.Value} was requested."
                : "This line has already been returned in full.");
        }

        return ReturnEligibility.Allowed.Instance;
    }

    /// <summary>
    /// SRS FR-5.19: a return with no original bill is disabled by default, and the service
    /// refuses outright while it is; enabled, every individual unlinked return still requires an
    /// owner override (task P2-T03 step 2) - so this never allows one without permission, whether
    /// the setting is on or off.
    /// </summary>
    /// <param name="isUnlinked">No original <c>sale</c> row could be matched to this return.</param>
    /// <param name="unlinkedReturnsAllowed"><c>policy.allow_unlinked_returns</c>.</param>
    public static ReturnEligibility EvaluateUnlinkedReturn(bool isUnlinked, bool unlinkedReturnsAllowed)
    {
        if (!isUnlinked)
        {
            return ReturnEligibility.Allowed.Instance;
        }

        return unlinkedReturnsAllowed
            ? new ReturnEligibility.AllowedWithOverride(
                "This return has no original bill. An owner has to authorise it, with a reason.")
            : new ReturnEligibility.Denied(
                "Returns without an original bill are disabled. Enable them in settings first, or "
                + "find the original bill.");
    }

    /// <summary>
    /// SRS FR-5.1, Q-03 ("should have to previous bill no"): even a return whose bill was found -
    /// by date, customer or amount rather than by scanning or typing its number - may be required
    /// to show the actual bill number before it counts as receipted.
    /// </summary>
    /// <param name="receiptRequired"><c>policy.receipt_required</c>.</param>
    /// <param name="billReferencePresented">
    /// The bill was identified by its own number - scanned or typed - rather than only found by a
    /// date/customer/amount search.
    /// </param>
    public static ReturnEligibility EvaluateReceiptRequirement(bool receiptRequired, bool billReferencePresented)
    {
        if (!receiptRequired || billReferencePresented)
        {
            return ReturnEligibility.Allowed.Instance;
        }

        return new ReturnEligibility.AllowedWithOverride(
            "A receipt (the bill number) is required for a return. An owner has to authorise this one.");
    }

    /// <summary>
    /// SRS FR-5.13: a cash refund above the configured limit needs an owner's authorisation.
    /// </summary>
    /// <param name="isCashRefund">The refund method chosen for this return is cash.</param>
    /// <param name="refundAmount">The refund amount, as a non-negative magnitude.</param>
    /// <param name="cashRefundLimit"><c>policy.cash_refund_limit</c>. Zero means no limit.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="refundAmount"/> is negative.</exception>
    public static ReturnEligibility EvaluateCashRefundLimit(bool isCashRefund, Money refundAmount, Money cashRefundLimit)
    {
        if (refundAmount.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refundAmount), refundAmount.Amount, "A refund amount is a magnitude, so it cannot be negative.");
        }

        if (!isCashRefund || cashRefundLimit.IsZero || refundAmount <= cashRefundLimit)
        {
            return ReturnEligibility.Allowed.Instance;
        }

        return new ReturnEligibility.AllowedWithOverride(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"A cash refund of {refundAmount} is above the {cashRefundLimit} limit. An owner has to authorise this."));
    }
}
