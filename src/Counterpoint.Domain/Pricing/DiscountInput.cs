using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// A discount as the cashier actually keys it in - a fixed amount off, or a percentage off (SRS
/// FR-3.11-ish, task P1-T08: "line discount and bill discount, as amount or percentage"). Kept
/// as one type rather than two optional fields on a command, so a caller can never construct one
/// with both an amount and a rate at once.
/// </summary>
public readonly record struct DiscountInput
{
    private DiscountInput(bool isRate, Money amount, Percentage rate)
    {
        IsRate = isRate;
        Amount = amount;
        Rate = rate;
    }

    /// <summary>True when this discount was keyed as a percentage; false for a fixed amount.</summary>
    public bool IsRate { get; }

    /// <summary>The amount, when <see cref="IsRate"/> is false. Zero otherwise.</summary>
    public Money Amount { get; }

    /// <summary>The rate, when <see cref="IsRate"/> is true. <see cref="Percentage.Zero"/> otherwise.</summary>
    public Percentage Rate { get; }

    /// <summary>A discount of a fixed amount off, for example "LKR 50 off".</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="amount"/> is negative.</exception>
    public static DiscountInput OfAmount(Money amount)
    {
        if (amount.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(amount),
                amount.Amount,
                "A discount is an amount taken off, so it cannot itself be negative.");
        }

        return new DiscountInput(isRate: false, amount, Percentage.Zero);
    }

    /// <summary>A discount of a percentage off, for example "10% off".</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rate"/> is negative.</exception>
    public static DiscountInput OfRate(Percentage rate)
    {
        if (rate.Fraction < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate),
                rate.Fraction,
                "A discount is a proportion taken off, so it cannot itself be negative.");
        }

        return new DiscountInput(isRate: true, Money.Zero, rate);
    }

    /// <summary>The discount in money, against <paramref name="baseAmount"/>. Unrounded (CLAUDE.md invariant 2).</summary>
    public Money ResolveAmount(Money baseAmount) => IsRate ? Rate.Of(baseAmount) : Amount;

    /// <summary>
    /// The discount as a rate of <paramref name="baseAmount"/> - what a cap is actually compared
    /// against, whether the cashier keyed an amount or a rate.
    /// </summary>
    /// <remarks>
    /// A fixed amount against a zero base has no meaningful rate; that reads as no discount at
    /// all rather than an infinite one; there is nothing to take a proportion of.
    /// </remarks>
    public Percentage ResolveRate(Money baseAmount)
    {
        if (IsRate)
        {
            return Rate;
        }

        return baseAmount.IsZero ? Percentage.Zero : Percentage.FromFraction(Amount.Amount / baseAmount.Amount);
    }
}
