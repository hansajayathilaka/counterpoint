using System;
using System.Globalization;

namespace Counterpoint.Domain.ValueObjects;

/// <summary>
/// The one place that knows how a <see cref="decimal"/> becomes the scaled 64-bit
/// integer that SQLite stores, and back again.
///
/// SRS DM-01 and DM-02, docs/01_DATA_MODEL.md §1: money, quantities and rates are all
/// stored as <c>INTEGER</c> scaled by 10 000. Keeping the conversion here means
/// <see cref="Money"/>, <see cref="Quantity"/>, <see cref="Percentage"/> and
/// <see cref="TaxRate"/> cannot drift apart on rounding or on the overflow boundary.
///
/// Every arithmetic step is <see cref="decimal"/>. Binary floating point is banned in
/// this project (CLAUDE.md invariant 1) and would silently lose cents here.
/// </summary>
internal static class ScaledDecimal
{
    /// <summary>Number of fractional digits the storage scale can represent.</summary>
    internal const int Places = 4;

    /// <summary>The storage scale itself: 12345678 stored is 1234.5678.</summary>
    internal const long Scale = 10_000L;

    /// <summary>
    /// Largest value that survives the round trip: <c>long.MaxValue / 10 000</c>,
    /// i.e. 9 223 372 036 854 775 807 scaled units.
    /// </summary>
    internal const decimal MaxRepresentable = 922_337_203_685_477.5807m;

    /// <summary>
    /// Smallest value that survives the round trip: <c>long.MinValue / 10 000</c>,
    /// i.e. -9 223 372 036 854 775 808 scaled units.
    /// </summary>
    internal const decimal MinRepresentable = -922_337_203_685_477.5808m;

    /// <summary>
    /// Quantises <paramref name="value"/> to the storage scale, half away from zero,
    /// and returns the scaled integer.
    /// </summary>
    /// <exception cref="OverflowException">
    /// The quantised value does not fit in a 64-bit scaled integer. It throws rather than
    /// wrapping: a wrapped total is a silently wrong bill, which is far worse than a crash.
    /// </exception>
    internal static long ToScaled(decimal value, string what)
    {
        // Quantise first: a value one ten-thousandth below the boundary must still fit,
        // and a value that rounds up past the boundary must not.
        var quantised = decimal.Round(value, Places, MidpointRounding.AwayFromZero);

        if (quantised < MinRepresentable || quantised > MaxRepresentable)
        {
            throw new OverflowException(string.Create(
                CultureInfo.InvariantCulture,
                $"The {what} {value} is outside the storable range "
                + $"{MinRepresentable} to {MaxRepresentable} once scaled by {Scale}."));
        }

        return decimal.ToInt64(quantised * Scale);
    }

    /// <summary>Turns a stored scaled integer back into an exact <see cref="decimal"/>.</summary>
    internal static decimal FromScaled(long scaled) => scaled / (decimal)Scale;

    /// <summary>Guards a decimal-place count against the limits of the storage scale.</summary>
    internal static int RequireStorablePlaces(int decimalPlaces, string parameterName)
    {
        if (decimalPlaces < 0 || decimalPlaces > Places)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                decimalPlaces,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Values are stored scaled by {Scale}, so only 0 to {Places} "
                    + $"decimal places can be represented."));
        }

        return decimalPlaces;
    }
}
