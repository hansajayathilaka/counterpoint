using System;
using System.Globalization;

namespace Counterpoint.Domain.ValueObjects;

/// <summary>
/// A quantity of something, in a stated unit of measure (SRS DM-02, CLAUDE.md invariant 1).
///
/// Stored as a 64-bit integer scaled by <see cref="QtyScale"/>, alongside the
/// <see cref="UomId"/> it was measured in. Stock and reports use base units; the
/// unit is carried here so that adding 3 coils to 2 metres cannot compile into a number.
///
/// Arithmetic between two quantities requires the same <see cref="UomId"/>. A mismatch
/// throws <see cref="InvalidOperationException"/> rather than a <c>DomainException</c>:
/// no cashier action can produce it and there is no plain-language message that would help
/// the shop. It is a programming error, and the exception type says so. Converting between
/// units is a separate, deliberate step (UOM conversion, P1-T05) — never an implicit one.
///
/// Equality is the exception to that rule: two quantities in different units are simply not
/// equal, so <c>==</c> answers <c>false</c> instead of throwing. Ordering has no such answer,
/// so <c>&lt;</c>, <c>&gt;</c> and <see cref="CompareTo(Quantity)"/> throw.
/// </summary>
public readonly record struct Quantity : IComparable<Quantity>
{
    /// <summary>Storage scale. <c>12345678</c> stored is <c>1234.5678</c>.</summary>
    public const long QtyScale = ScaledDecimal.Scale;

    /// <summary>Fractional digits the storage scale can represent.</summary>
    public const int QtyDecimalPlaces = ScaledDecimal.Places;

    private Quantity(decimal value, long uomId)
    {
        Value = value;
        UomId = uomId;
    }

    /// <summary>How many, in <see cref="UomId"/>.</summary>
    public decimal Value { get; }

    /// <summary>The <c>uom.id</c> this quantity is measured in.</summary>
    public long UomId { get; }

    /// <summary>True when the quantity is below zero — an issue, a return, a write-off.</summary>
    public bool IsNegative => Value < 0m;

    /// <summary>True when the quantity is above zero.</summary>
    public bool IsPositive => Value > 0m;

    /// <summary>True when the quantity is exactly zero.</summary>
    public bool IsZero => Value == 0m;

    /// <summary>Nothing, in the given unit.</summary>
    public static Quantity Zero(long uomId) => new(0m, uomId);

    /// <summary>Wraps a decimal quantity in a unit. No rounding is applied.</summary>
    public static Quantity FromDecimal(decimal value, long uomId) => new(value, uomId);

    /// <summary>Reads a quantity back from its stored scaled integer form. Always exact.</summary>
    public static Quantity FromScaled(long scaled, long uomId) =>
        new(ScaledDecimal.FromScaled(scaled), uomId);

    /// <summary>
    /// Converts to the scaled integer SQLite stores, quantising half away from zero.
    /// </summary>
    /// <exception cref="OverflowException">
    /// The quantity is outside ±922 337 203 685 477.5807. It throws rather than wrapping.
    /// </exception>
    public long ToScaled() => ScaledDecimal.ToScaled(Value, "quantity");

    /// <summary>The absolute quantity, in the same unit.</summary>
    public Quantity Abs() => new(Math.Abs(Value), UomId);

    /// <summary>The quantity with its sign flipped, in the same unit.</summary>
    public Quantity Negate() => new(-Value, UomId);

    /// <summary>Named alternate for <c>operator +</c>.</summary>
    public Quantity Add(Quantity other) => this + other;

    /// <summary>Named alternate for <c>operator -</c>.</summary>
    public Quantity Subtract(Quantity other) => this - other;

    /// <summary>Named alternate for <c>operator *</c>. Scaling by a factor keeps the unit.</summary>
    public Quantity Multiply(decimal factor) => new(Value * factor, UomId);

    /// <summary>Named alternate for <c>operator /</c>. Scaling by a divisor keeps the unit.</summary>
    public Quantity Divide(decimal divisor) => new(Value / divisor, UomId);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public int CompareTo(Quantity other)
    {
        RequireSameUom(this, other, "compare");
        return Value.CompareTo(other.Value);
    }

    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public static Quantity operator +(Quantity left, Quantity right)
    {
        RequireSameUom(left, right, "add");
        return new Quantity(left.Value + right.Value, left.UomId);
    }

    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public static Quantity operator -(Quantity left, Quantity right)
    {
        RequireSameUom(left, right, "subtract");
        return new Quantity(left.Value - right.Value, left.UomId);
    }

    public static Quantity operator -(Quantity value) => value.Negate();

    public static Quantity operator *(Quantity left, decimal right) => new(left.Value * right, left.UomId);

    public static Quantity operator *(decimal left, Quantity right) => new(left * right.Value, right.UomId);

    public static Quantity operator /(Quantity left, decimal right) => new(left.Value / right, left.UomId);

    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public static bool operator <(Quantity left, Quantity right) => left.CompareTo(right) < 0;

    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public static bool operator >(Quantity left, Quantity right) => left.CompareTo(right) > 0;

    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public static bool operator <=(Quantity left, Quantity right) => left.CompareTo(right) <= 0;

    /// <exception cref="InvalidOperationException">The units differ.</exception>
    public static bool operator >=(Quantity left, Quantity right) => left.CompareTo(right) >= 0;

    /// <summary>Culture-invariant, for logs and tests.</summary>
    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Value} (uom {UomId})");

    private static void RequireSameUom(Quantity left, Quantity right, string operation)
    {
        if (left.UomId != right.UomId)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Cannot {operation} quantities in different units of measure "
                + $"(uom {left.UomId} and uom {right.UomId}). Convert to a common unit first."));
        }
    }
}
