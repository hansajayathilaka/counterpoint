using System;
using System.Globalization;

namespace Counterpoint.Domain.ValueObjects;

/// <summary>
/// A <c>product_uom.conversion_factor</c>: how many base units one of some other unit is worth
/// (docs/01_DATA_MODEL.md §3, §8, SRS FR-2.4, FR-2.5). <c>1 box = 100 pieces</c> is a factor of
/// <c>100</c>.
///
/// Stored as a 64-bit integer scaled by <see cref="FactorScale"/>, the same convention as
/// <see cref="Money"/> and <see cref="Quantity"/> (CLAUDE.md invariant 1). Kept distinct from
/// both: it is a ratio between two units, not an amount of one of them, so it carries no
/// <c>uom_id</c> of its own and takes no part in addition or subtraction - only multiplication
/// and division, through <c>Counterpoint.Domain.Catalogue.UomConverter</c>.
/// </summary>
public readonly record struct UomConversion : IComparable<UomConversion>
{
    /// <summary>Storage scale. <c>1000000</c> stored is a factor of <c>100</c>.</summary>
    public const long FactorScale = ScaledDecimal.Scale;

    /// <summary>Fractional digits the storage scale can represent.</summary>
    public const int FactorDecimalPlaces = ScaledDecimal.Places;

    private UomConversion(decimal factor) => Factor = factor;

    /// <summary>How many base units one of this unit is worth.</summary>
    public decimal Factor { get; }

    /// <summary>
    /// The base unit's own conversion into itself: a factor of exactly <c>1</c>
    /// (<c>product_uom.is_base = 1</c> rows are the schema's own base-unit guard, §8).
    /// </summary>
    public static UomConversion Base => new(1m);

    /// <summary>True for the base unit's own conversion factor.</summary>
    public bool IsBase => Factor == 1m;

    /// <summary>Builds from a plain factor, for example <c>100m</c> for "1 box = 100 pieces".</summary>
    /// <exception cref="ArgumentOutOfRangeException">The factor is not positive.</exception>
    public static UomConversion FromDecimal(decimal factor) => new(RequirePositive(factor));

    /// <summary>Reads a factor back from its stored scaled integer form. Always exact.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The factor is not positive.</exception>
    public static UomConversion FromScaled(long scaled) => new(RequirePositive(ScaledDecimal.FromScaled(scaled)));

    /// <summary>
    /// Converts to the scaled integer <c>product_uom.conversion_factor</c> stores, quantising
    /// half away from zero.
    /// </summary>
    /// <exception cref="OverflowException">The factor does not fit a 64-bit scaled integer.</exception>
    public long ToScaled() => ScaledDecimal.ToScaled(Factor, "uom conversion factor");

    /// <inheritdoc />
    public int CompareTo(UomConversion other) => Factor.CompareTo(other.Factor);

    public static bool operator <(UomConversion left, UomConversion right) => left.Factor < right.Factor;

    public static bool operator >(UomConversion left, UomConversion right) => left.Factor > right.Factor;

    public static bool operator <=(UomConversion left, UomConversion right) => left.Factor <= right.Factor;

    public static bool operator >=(UomConversion left, UomConversion right) => left.Factor >= right.Factor;

    /// <summary>Culture-invariant, for logs and tests.</summary>
    public override string ToString() => Factor.ToString(CultureInfo.InvariantCulture);

    private static decimal RequirePositive(decimal factor)
    {
        if (factor <= 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(factor),
                factor,
                "A unit-of-measure conversion factor must be positive "
                + "(ck_product_uom_conversion_factor).");
        }

        return factor;
    }
}
