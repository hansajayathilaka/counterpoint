using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// What a requested discount comes to, and whether it fits inside its cap (SRS FR-2.13-FR-2.19
/// area, Q-12, task P1-T08 step 2).
/// </summary>
/// <param name="Amount">The discount in money (<see cref="DiscountInput.ResolveAmount"/>).</param>
/// <param name="Rate">The discount as a proportion of the base amount (<see cref="DiscountInput.ResolveRate"/>).</param>
/// <param name="Cap">
/// The limit <see cref="Rate"/> was checked against - <c>product.max_discount_rate</c> if the
/// product has one, otherwise the shop's policy limit (<c>policy.max_line_discount_rate</c> or
/// <c>policy.max_bill_discount_rate</c>, Q-12).
/// </param>
/// <param name="ExceedsCap">True when <see cref="Rate"/> is above <see cref="Cap"/> - an owner override is required to apply it.</param>
public sealed record DiscountEvaluation(Money Amount, Percentage Rate, Percentage Cap, bool ExceedsCap);
