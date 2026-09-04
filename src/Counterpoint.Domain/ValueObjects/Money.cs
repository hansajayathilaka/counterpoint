using System;
using System.Globalization;

namespace Counterpoint.Domain.ValueObjects;

/// <summary>
/// A monetary amount (SRS DM-01, CLAUDE.md invariant 1).
///
/// Stored as a 64-bit integer scaled by <see cref="MoneyScale"/>; held in memory as a
/// <see cref="decimal"/>. Every arithmetic operation in the shop's money path goes through
/// this type, so there is no code path where a price becomes binary floating point.
///
/// The value is <em>not</em> quantised on construction. Intermediate results — a unit price
/// times a fractional quantity, a proportional discount — keep full decimal precision, and
/// rounding to the currency's decimal places happens at exactly two points, line total and
/// bill total, through <c>IRoundingPolicy</c> (CLAUDE.md invariant 2). Quantisation to the
/// storage scale happens once more, in <see cref="ToScaled"/>, on the way to the database.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    /// <summary>Storage scale. <c>12345678</c> stored is <c>1234.5678</c>.</summary>
    public const long MoneyScale = ScaledDecimal.Scale;

    /// <summary>Fractional digits the storage scale can represent.</summary>
    public const int MoneyDecimalPlaces = ScaledDecimal.Places;

    private Money(decimal amount) => Amount = amount;

    /// <summary>The amount, in currency units.</summary>
    public decimal Amount { get; }

    /// <summary>Nothing owed, nothing paid.</summary>
    public static Money Zero => default;

    /// <summary>The largest amount that can be stored: 922 337 203 685 477.5807.</summary>
    public static Money MaxValue => FromScaled(long.MaxValue);

    /// <summary>The smallest amount that can be stored: -922 337 203 685 477.5808.</summary>
    public static Money MinValue => FromScaled(long.MinValue);

    /// <summary>True when the amount is below zero — a refund, a credit, an over-tender.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>True when the amount is above zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>True when the amount is exactly zero.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>Wraps a decimal amount. No rounding is applied.</summary>
    public static Money FromDecimal(decimal amount) => new(amount);

    /// <summary>Reads an amount back from its stored scaled integer form. Always exact.</summary>
    public static Money FromScaled(long scaled) => new(ScaledDecimal.FromScaled(scaled));

    /// <summary>
    /// Converts to the scaled integer SQLite stores, quantising half away from zero.
    /// </summary>
    /// <exception cref="OverflowException">
    /// The amount is outside ±922 337 203 685 477.5807. It throws rather than wrapping.
    /// </exception>
    public long ToScaled() => ScaledDecimal.ToScaled(Amount, "money amount");

    /// <summary>The absolute amount.</summary>
    public Money Abs() => new(Math.Abs(Amount));

    /// <summary>The amount with its sign flipped.</summary>
    public Money Negate() => new(-Amount);

    /// <summary>Named alternate for <c>operator +</c>.</summary>
    public Money Add(Money other) => new(Amount + other.Amount);

    /// <summary>Named alternate for <c>operator -</c>.</summary>
    public Money Subtract(Money other) => new(Amount - other.Amount);

    /// <summary>Named alternate for <c>operator *</c>.</summary>
    public Money Multiply(decimal factor) => new(Amount * factor);

    /// <summary>Named alternate for <c>operator /</c>.</summary>
    public Money Divide(decimal divisor) => new(Amount / divisor);

    /// <inheritdoc />
    public int CompareTo(Money other) => Amount.CompareTo(other.Amount);

    public static Money operator +(Money left, Money right) => new(left.Amount + right.Amount);

    public static Money operator -(Money left, Money right) => new(left.Amount - right.Amount);

    public static Money operator -(Money value) => new(-value.Amount);

    public static Money operator *(Money left, decimal right) => new(left.Amount * right);

    public static Money operator *(decimal left, Money right) => new(left * right.Amount);

    public static Money operator /(Money left, decimal right) => new(left.Amount / right);

    public static bool operator <(Money left, Money right) => left.Amount < right.Amount;

    public static bool operator >(Money left, Money right) => left.Amount > right.Amount;

    public static bool operator <=(Money left, Money right) => left.Amount <= right.Amount;

    public static bool operator >=(Money left, Money right) => left.Amount >= right.Amount;

    /// <summary>Culture-invariant, for logs, tests and canonical JSON — never for the till display.</summary>
    public override string ToString() => Amount.ToString(CultureInfo.InvariantCulture);
}
