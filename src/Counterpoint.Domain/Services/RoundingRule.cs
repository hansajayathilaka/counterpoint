namespace Counterpoint.Domain.Services;

/// <summary>
/// How the shop rounds a computed amount to its currency's decimal places
/// (SRS FR-10.2: "rounding rule" is a setting, not a build-time decision).
/// </summary>
/// <remarks>
/// The rule and the number of decimal places together make an <see cref="IRoundingPolicy"/>
/// through <see cref="RoundingPolicyFactory"/>. Rounding still happens at exactly two points -
/// the line total and the bill total (CLAUDE.md invariant 2); this only says which way a
/// midpoint goes.
/// </remarks>
public enum RoundingRule
{
    /// <summary>
    /// Half away from zero: 0.005 becomes 0.01, -0.005 becomes -0.01. The shopkeeper's rule,
    /// and the default, because it makes a return the exact mirror of the sale it reverses.
    /// </summary>
    HalfAwayFromZero = 0,

    /// <summary>
    /// Half to even, "banker's rounding": 0.125 becomes 0.12, 0.135 becomes 0.14. Available
    /// because some jurisdictions require it; not the default (see
    /// <see cref="HalfAwayFromZeroRounding"/> for why).
    /// </summary>
    HalfToEven = 1,
}
