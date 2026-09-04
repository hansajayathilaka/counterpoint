using System;
using System.Globalization;

namespace Counterpoint.Domain.ValueObjects;

/// <summary>
/// A proportion — a discount rate, a markup, a margin (docs/01_DATA_MODEL.md §1).
///
/// Stored as a 64-bit integer scaled by <see cref="RateScale"/> over the <em>fraction</em>,
/// not over the percent: <c>1500</c> is 15.00%, i.e. 0.1500. That is the convention the
/// schema uses for every rate column, and mixing the two up is the classic hundred-fold
/// pricing bug, so the type keeps the fraction and offers the percent as a projection.
/// </summary>
public readonly record struct Percentage : IComparable<Percentage>
{
    /// <summary>Storage scale over the fraction. <c>1500</c> stored is 15.00%.</summary>
    public const long RateScale = ScaledDecimal.Scale;

    /// <summary>Fractional digits the storage scale can represent.</summary>
    public const int RateDecimalPlaces = ScaledDecimal.Places;

    private Percentage(decimal fraction) => Fraction = fraction;

    /// <summary>The proportion as a fraction: 0.15 for 15%.</summary>
    public decimal Fraction { get; }

    /// <summary>The same proportion expressed in percent: 15 for 15%.</summary>
    public decimal AsPercent => Fraction * 100m;

    /// <summary>No discount, no markup.</summary>
    public static Percentage Zero => default;

    /// <summary>The whole thing: 100%.</summary>
    public static Percentage OneHundredPercent => new(1m);

    /// <summary>True when the proportion is above zero.</summary>
    public bool IsPositive => Fraction > 0m;

    /// <summary>True when the proportion is exactly zero.</summary>
    public bool IsZero => Fraction == 0m;

    /// <summary>Builds from a fraction: <c>0.15m</c> for 15%.</summary>
    public static Percentage FromFraction(decimal fraction) => new(fraction);

    /// <summary>Builds from a percent: <c>15m</c> for 15%.</summary>
    public static Percentage FromPercent(decimal percent) => new(percent / 100m);

    /// <summary>Reads back from the stored scaled integer: <c>1500</c> is 15%.</summary>
    public static Percentage FromScaled(long scaled) => new(ScaledDecimal.FromScaled(scaled));

    /// <summary>
    /// Converts to the scaled integer stored in a rate column, quantising half away from zero.
    /// </summary>
    /// <exception cref="OverflowException">The fraction does not fit a 64-bit scaled integer.</exception>
    public long ToScaled() => ScaledDecimal.ToScaled(Fraction, "percentage");

    /// <summary>
    /// This proportion of an amount, <strong>unrounded</strong>. Rounding belongs to the
    /// line total and the bill total only, through <c>IRoundingPolicy</c>
    /// (CLAUDE.md invariant 2), so this deliberately does not round.
    /// </summary>
    public Money Of(Money amount) => amount * Fraction;

    /// <summary>What is left after taking this proportion off, <strong>unrounded</strong>.</summary>
    public Money RemainderOf(Money amount) => amount * (1m - Fraction);

    /// <inheritdoc />
    public int CompareTo(Percentage other) => Fraction.CompareTo(other.Fraction);

    public static bool operator <(Percentage left, Percentage right) => left.Fraction < right.Fraction;

    public static bool operator >(Percentage left, Percentage right) => left.Fraction > right.Fraction;

    public static bool operator <=(Percentage left, Percentage right) => left.Fraction <= right.Fraction;

    public static bool operator >=(Percentage left, Percentage right) => left.Fraction >= right.Fraction;

    /// <summary>Culture-invariant, for logs and tests.</summary>
    public override string ToString() => AsPercent.ToString("0.####", CultureInfo.InvariantCulture) + "%";
}
