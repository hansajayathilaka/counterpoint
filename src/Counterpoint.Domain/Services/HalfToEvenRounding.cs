using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Services;

/// <summary>
/// Banker's rounding to a configured number of decimal places (SRS FR-10.2,
/// <see cref="RoundingRule.HalfToEven"/>): a midpoint goes to the nearer even digit, so 0.125
/// becomes 0.12 and 0.135 becomes 0.14.
///
/// Offered because the rounding rule is the shop's to choose, not this build's. It is not the
/// default: to-even is defensible statistically but surprises people at a counter, and it makes
/// a refund fail to match the receipt it came from. See <see cref="HalfAwayFromZeroRounding"/>.
///
/// The arithmetic is <see cref="decimal"/>, never binary floating point (CLAUDE.md invariant 1).
/// </summary>
public sealed class HalfToEvenRounding : IRoundingPolicy
{
    /// <summary>
    /// Rounding to more places than the storage scale carries would be a lie, so the configured
    /// places are capped at the scale (four).
    /// </summary>
    public const int MaxDecimalPlaces = Money.MoneyDecimalPlaces;

    /// <summary>
    /// Creates the policy for a currency with <paramref name="decimalPlaces"/> minor digits. The
    /// value comes from settings (FR-10.2); it is passed in rather than read here so the Domain
    /// stays free of configuration.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="decimalPlaces"/> is negative or above <see cref="MaxDecimalPlaces"/>.
    /// </exception>
    public HalfToEvenRounding(int decimalPlaces) =>
        DecimalPlaces = ScaledDecimal.RequireStorablePlaces(decimalPlaces, nameof(decimalPlaces));

    /// <inheritdoc />
    public int DecimalPlaces { get; }

    /// <inheritdoc />
    public Money Round(Money amount) =>
        Money.FromDecimal(decimal.Round(amount.Amount, DecimalPlaces, MidpointRounding.ToEven));
}
