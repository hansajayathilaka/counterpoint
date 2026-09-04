using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Services;

/// <summary>
/// The default rounding rule (SRS FR-10.2): half away from zero, to a configured number of
/// decimal places. 0.005 becomes 0.01 and -0.005 becomes -0.01 at two places — the rule a
/// shopkeeper expects, and the one that keeps a return the exact mirror of the sale it
/// reverses.
///
/// Deliberately not banker's rounding: to-even is defensible statistically but surprises
/// people at a counter, and it makes a refund fail to match the receipt it came from.
///
/// The arithmetic is <see cref="decimal"/> with <see cref="MidpointRounding.AwayFromZero"/>.
/// Binary floating point would round 2.675 to 2.67 because 2.675 is not representable;
/// decimal rounds it to 2.68 (CLAUDE.md invariant 1).
/// </summary>
public sealed class HalfAwayFromZeroRounding : IRoundingPolicy
{
    /// <summary>
    /// Rounding to more places than the storage scale carries would be a lie, so the
    /// configured places are capped at the scale (four).
    /// </summary>
    public const int MaxDecimalPlaces = Money.MoneyDecimalPlaces;

    /// <summary>
    /// Creates the policy for a currency with <paramref name="decimalPlaces"/> minor digits.
    /// The value comes from settings (FR-10.2); it is passed in rather than read here so the
    /// Domain stays free of configuration.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="decimalPlaces"/> is negative or above <see cref="MaxDecimalPlaces"/>.
    /// </exception>
    public HalfAwayFromZeroRounding(int decimalPlaces) =>
        DecimalPlaces = ScaledDecimal.RequireStorablePlaces(decimalPlaces, nameof(decimalPlaces));

    /// <inheritdoc />
    public int DecimalPlaces { get; }

    /// <inheritdoc />
    public Money Round(Money amount) =>
        Money.FromDecimal(decimal.Round(amount.Amount, DecimalPlaces, MidpointRounding.AwayFromZero));
}
