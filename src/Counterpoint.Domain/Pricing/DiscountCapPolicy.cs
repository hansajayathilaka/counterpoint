using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// How far a discount is allowed to go before it needs an owner's say-so (SRS FR-1.7, Q-12, task
/// P1-T08 step 2: "capped by <c>product.max_discount_rate</c> then the global cashier limit").
/// </summary>
/// <remarks>
/// Pure evaluation only - it never refuses a discount and it knows nothing about override tokens
/// or audit rows. <c>Counterpoint.Application.Pricing.IDiscountAuthorisationService</c> is the
/// half of this that actually stops an over-cap discount without a granted override; this is the
/// arithmetic that decision is based on, kept separate so the rule "the product's own limit wins
/// over the shop-wide one" is provable without touching settings, sessions or the database.
/// </remarks>
public static class DiscountCapPolicy
{
    /// <summary>
    /// The limit a discount is actually checked against: the product's own cap if it has one,
    /// otherwise the shop-wide policy limit.
    /// </summary>
    public static Percentage EffectiveCap(Percentage? productMaxDiscountRate, Percentage policyLimit) =>
        productMaxDiscountRate ?? policyLimit;

    /// <summary>
    /// Evaluates <paramref name="discount"/> against <paramref name="baseAmount"/> and the
    /// effective cap.
    /// </summary>
    /// <param name="discount">What the cashier asked for.</param>
    /// <param name="baseAmount">The line total or bill subtotal the discount is taken off.</param>
    /// <param name="productMaxDiscountRate"><c>product.max_discount_rate</c>, or null to use <paramref name="policyLimit"/>.</param>
    /// <param name="policyLimit">
    /// <c>policy.max_line_discount_rate</c> or <c>policy.max_bill_discount_rate</c>, whichever
    /// this discount is for.
    /// </param>
    public static DiscountEvaluation Evaluate(
        DiscountInput discount,
        Money baseAmount,
        Percentage? productMaxDiscountRate,
        Percentage policyLimit)
    {
        var cap = EffectiveCap(productMaxDiscountRate, policyLimit);
        var rate = discount.ResolveRate(baseAmount);
        var amount = discount.ResolveAmount(baseAmount);

        return new DiscountEvaluation(amount, rate, cap, rate > cap);
    }
}
