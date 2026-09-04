using System;
using System.Globalization;

namespace Counterpoint.Domain.ValueObjects;

/// <summary>
/// The rate on a <c>tax_class</c> (docs/01_DATA_MODEL.md, <c>tax_class.rate</c>).
///
/// Stored as a 64-bit integer scaled by <see cref="RateScale"/> over the fraction:
/// <c>1500</c> is 15%. Kept distinct from <see cref="Percentage"/> because a tax rate is
/// never negative and because the two ways of applying it — on top of a net price, or
/// carved out of a tax-inclusive price — are tax-specific and easy to get backwards.
///
/// Both calculations return an <strong>unrounded</strong> <see cref="Money"/>. Rounding
/// happens at the line total and the bill total, through <c>IRoundingPolicy</c>, and
/// nowhere else (CLAUDE.md invariant 2). Bill tax is the sum of line tax; it is never
/// recomputed from the bill total.
/// </summary>
public readonly record struct TaxRate : IComparable<TaxRate>
{
    /// <summary>Storage scale over the fraction. <c>1500</c> stored is 15%.</summary>
    public const long RateScale = ScaledDecimal.Scale;

    /// <summary>Fractional digits the storage scale can represent.</summary>
    public const int RateDecimalPlaces = ScaledDecimal.Places;

    private TaxRate(decimal rate) => Rate = rate;

    /// <summary>The rate as a fraction: 0.15 for 15%.</summary>
    public decimal Rate { get; }

    /// <summary>The same rate expressed in percent: 15 for 15%.</summary>
    public decimal AsPercent => Rate * 100m;

    /// <summary>A zero-rated tax class.</summary>
    public static TaxRate Zero => default;

    /// <summary>True for a zero-rated or exempt class.</summary>
    public bool IsZero => Rate == 0m;

    /// <summary>Builds from a fraction: <c>0.15m</c> for 15%.</summary>
    public static TaxRate FromFraction(decimal rate) => new(RequireNotNegative(rate));

    /// <summary>Builds from a percent: <c>15m</c> for 15%.</summary>
    public static TaxRate FromPercent(decimal percent) => new(RequireNotNegative(percent / 100m));

    /// <summary>Reads back from the stored <c>tax_class.rate</c>: <c>1500</c> is 15%.</summary>
    public static TaxRate FromScaled(long scaled) => new(RequireNotNegative(ScaledDecimal.FromScaled(scaled)));

    /// <summary>
    /// Converts to the scaled integer stored in <c>tax_class.rate</c>, quantising half away from zero.
    /// </summary>
    /// <exception cref="OverflowException">The rate does not fit a 64-bit scaled integer.</exception>
    public long ToScaled() => ScaledDecimal.ToScaled(Rate, "tax rate");

    /// <summary>
    /// Tax-exclusive: the tax added on top of a net amount, <strong>unrounded</strong>.
    /// </summary>
    public Money TaxOnNet(Money net) => net * Rate;

    /// <summary>
    /// Tax-exclusive: net plus its tax, <strong>unrounded</strong>.
    /// </summary>
    public Money GrossFromNet(Money net) => net * (1m + Rate);

    /// <summary>
    /// Tax-inclusive: the tax already contained in a gross amount, <strong>unrounded</strong>.
    /// <c>gross - gross / (1 + rate)</c>.
    /// </summary>
    public Money TaxWithinGross(Money gross) => gross - (gross / (1m + Rate));

    /// <summary>
    /// Tax-inclusive: the net amount inside a gross amount, <strong>unrounded</strong>.
    /// </summary>
    public Money NetFromGross(Money gross) => gross / (1m + Rate);

    /// <inheritdoc />
    public int CompareTo(TaxRate other) => Rate.CompareTo(other.Rate);

    public static bool operator <(TaxRate left, TaxRate right) => left.Rate < right.Rate;

    public static bool operator >(TaxRate left, TaxRate right) => left.Rate > right.Rate;

    public static bool operator <=(TaxRate left, TaxRate right) => left.Rate <= right.Rate;

    public static bool operator >=(TaxRate left, TaxRate right) => left.Rate >= right.Rate;

    /// <summary>Culture-invariant, for logs and tests.</summary>
    public override string ToString() => AsPercent.ToString("0.####", CultureInfo.InvariantCulture) + "%";

    private static decimal RequireNotNegative(decimal rate)
    {
        if (rate < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rate),
                rate,
                "A tax rate cannot be negative. A relief or a rebate is a discount, not a tax.");
        }

        return rate;
    }
}
