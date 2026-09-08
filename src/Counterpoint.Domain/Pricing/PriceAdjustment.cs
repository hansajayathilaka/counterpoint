using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// A bulk price change, "by percentage or fixed amount" (SRS FR-2.19). Signed: a positive rate
/// or amount raises prices, a negative one lowers them - one type for both directions of the
/// task's "increase or decrease", the same way <see cref="DiscountInput"/> is one type for "amount
/// or percentage".
/// </summary>
public readonly record struct PriceAdjustment
{
    private PriceAdjustment(bool isRate, Money amount, Percentage rate)
    {
        IsRate = isRate;
        Amount = amount;
        Rate = rate;
    }

    /// <summary>True for a percentage adjustment; false for a fixed amount.</summary>
    public bool IsRate { get; }

    /// <summary>The signed amount, when <see cref="IsRate"/> is false.</summary>
    public Money Amount { get; }

    /// <summary>The signed rate, when <see cref="IsRate"/> is true. 0.10 raises prices 10%; -0.10 lowers them 10%.</summary>
    public Percentage Rate { get; }

    /// <summary>Adjusts every price by <paramref name="rate"/>, for example -10% for a sale.</summary>
    public static PriceAdjustment ByPercentage(Percentage rate) => new(isRate: true, Money.Zero, rate);

    /// <summary>Adjusts every price by a fixed amount, for example -LKR 50 off everything in a category.</summary>
    public static PriceAdjustment ByFixedAmount(Money amount) => new(isRate: false, amount, Percentage.Zero);

    /// <summary>
    /// The new price for <paramref name="currentPrice"/>, quantised to the storage scale - the
    /// same quantisation every price written to <c>product_variant.price</c> already goes through
    /// on the way to the database, not a third rounding-policy point (CLAUDE.md invariant 2 is
    /// about the line total and the bill total; this writes a catalogue price, the same as an
    /// owner typing one in by hand).
    /// </summary>
    public Money ApplyTo(Money currentPrice)
    {
        var adjusted = IsRate ? currentPrice * (1m + Rate.Fraction) : currentPrice + Amount;

        return Money.FromScaled(adjusted.ToScaled());
    }
}
