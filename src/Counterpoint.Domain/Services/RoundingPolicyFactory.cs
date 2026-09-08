using System;
using System.ComponentModel;

namespace Counterpoint.Domain.Services;

/// <summary>
/// Turns the shop's two financial settings - the rounding rule and the currency's decimal places
/// (SRS FR-10.2) - into the <see cref="IRoundingPolicy"/> the line total and the bill total are
/// rounded with.
/// </summary>
/// <remarks>
/// The only place in the solution that constructs a rounding policy from a rule. Everywhere else
/// asks for <see cref="IRoundingPolicy"/> and is handed one built from the persisted settings, so
/// a decimal-place count is never written into code (P1-T03 "no hard-coded currency symbol,
/// tax rate or discount limit anywhere").
/// </remarks>
public static class RoundingPolicyFactory
{
    /// <summary>Builds the policy for a rule and a decimal-place count.</summary>
    /// <exception cref="InvalidEnumArgumentException"><paramref name="rule"/> is not a known rule.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="decimalPlaces"/> is negative or beyond the storage scale.
    /// </exception>
    public static IRoundingPolicy Create(RoundingRule rule, int decimalPlaces) => rule switch
    {
        RoundingRule.HalfAwayFromZero => new HalfAwayFromZeroRounding(decimalPlaces),
        RoundingRule.HalfToEven => new HalfToEvenRounding(decimalPlaces),
        _ => throw new InvalidEnumArgumentException(nameof(rule), (int)rule, typeof(RoundingRule)),
    };
}
