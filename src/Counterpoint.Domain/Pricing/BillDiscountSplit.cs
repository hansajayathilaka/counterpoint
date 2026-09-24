using System;
using System.Collections.Generic;
using System.Linq;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// How much of a bill-level discount (SRS FR-3.17) falls on each line of the bill.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a line needs to know.</b> The bill discount reduces what the customer pays for every
/// line on the bill, so it reduces the tax base of every line (SRS §10.1: "Taxable value" is the
/// sub total <em>less</em> the discount), and a later return of one line must refund what was
/// actually paid for it, not its pre-discount price (SRS FR-5, AC-03).
/// </para>
/// <para>
/// <b>Recomputable from the stored row, byte for byte.</b> The weights are
/// <c>unit_price x qty - discount</c> - exactly the three <c>sale_line</c> columns
/// (invariant 10's snapshots) - taken unrounded, so the split never depends on a rounding
/// setting that may have changed since the bill was sold. The sale path splits the discount with
/// these weights when it prices the bill; a return, a reprint or a report recomputes the same
/// weights from the same columns and, through the same deterministic
/// <see cref="DiscountAllocator"/>, gets the same split back. Nothing extra has to be stored.
/// </para>
/// </remarks>
public static class BillDiscountSplit
{
    /// <summary>
    /// A line's share of the bill, before the bill discount: what it was priced at, less its own
    /// line discount. Never negative - a line discounted to nothing carries no share.
    /// </summary>
    /// <remarks>
    /// Every input is quantised to the storage scale first, so the sale path - which holds these
    /// figures in memory, possibly with more places than a column keeps (an alternate unit's
    /// derived price, a percentage discount) - computes exactly the weight a later reader
    /// recomputes from the stored row.
    /// </remarks>
    /// <param name="unitPrice"><c>sale_line.unit_price</c>, per selling unit.</param>
    /// <param name="quantity"><c>sale_line.qty</c>, in the selling unit.</param>
    /// <param name="lineDiscount"><c>sale_line.discount</c>.</param>
    public static Money Weight(Money unitPrice, Quantity quantity, Money lineDiscount)
    {
        var weight = (Money.FromScaled(unitPrice.ToScaled()) * Quantity.FromScaled(quantity.ToScaled(), quantity.UomId).Value)
            - Money.FromScaled(lineDiscount.ToScaled());
        return weight.IsNegative ? Money.Zero : weight;
    }

    /// <summary>
    /// Splits <paramref name="billDiscount"/> across lines in proportion to their
    /// <paramref name="weights"/>, summing to the discount exactly.
    /// </summary>
    /// <returns>One share per weight, in the same order. All zero when there is no discount.</returns>
    public static IReadOnlyList<Money> Allocate(Money billDiscount, IReadOnlyList<Money> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);

        if (billDiscount.IsZero || weights.Count == 0)
        {
            return [.. weights.Select(_ => Money.Zero)];
        }

        return DiscountAllocator.Allocate(billDiscount, weights);
    }
}
