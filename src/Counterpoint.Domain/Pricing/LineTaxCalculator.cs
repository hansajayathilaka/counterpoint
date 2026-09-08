using System;
using Counterpoint.Domain.Services;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Pricing;

/// <summary>
/// Prices one bill line's tax, for both tax-exclusive and tax-inclusive shops (SRS FR-10.3, task
/// P1-T08 step 4).
/// </summary>
/// <remarks>
/// <para>
/// <b>One rounding point, whichever mode the shop is in.</b> <paramref name="unitPrice"/> times
/// the quantity is rounded exactly once, through <see cref="IRoundingPolicy"/> - the line-total
/// rounding point CLAUDE.md invariant 2 allows. In an exclusive shop that rounded figure
/// <em>is</em> the net line total and tax is added notionally on top of it (never itself
/// rounded). In an inclusive shop it is the gross amount actually charged for the line, and tax
/// is the amount already sitting inside it, carved out as <c>gross - gross / (1 + rate)</c> (the
/// documented risk in P1-T08's own task file). Either way, tax is quantised to the storage
/// scale once - the same quantisation <see cref="Money.ToScaled"/> applies to every amount on its
/// way to a row, not a second application of the rounding policy - so that
/// <c>line_total + tax</c> reconstructs the amount actually charged over the values as stored,
/// not merely as decimals in memory.
/// </para>
/// <para>
/// <see cref="LinePricing.LineTotal"/> is always the <em>net</em> figure, in both modes. That is
/// what makes <c>sale.total = sale.subtotal - sale.bill_discount + sale.tax + sale.rounding</c>
/// true regardless of whether prices include tax: an inclusive shop's <c>unit_price</c> is the
/// gross, "as charged" figure (docs/01_DATA_MODEL.md §3, <c>sale_line.unit_price</c>), but
/// <c>line_total</c> is always what is left once that line's tax is taken back out, so tax is
/// never added twice.
/// </para>
/// </remarks>
public static class LineTaxCalculator
{
    /// <summary>
    /// Prices one line.
    /// </summary>
    /// <param name="unitPrice">
    /// What one unit sells for, as the shop prices it - net of tax in an exclusive shop, gross of
    /// tax (the price the customer actually sees) in an inclusive one.
    /// </param>
    /// <param name="quantity">How many units, in whatever unit <paramref name="unitPrice"/> is for.</param>
    /// <param name="taxRate">The rate on the product's tax class. Zero is a valid rate - an exempt or zero-rated item.</param>
    /// <param name="pricesIncludeTax"><c>tax.prices_include_tax</c> (SRS FR-10.3) - the shop-wide switch, never a per-line one.</param>
    /// <param name="rounding">The line-total rounding point (CLAUDE.md invariant 2).</param>
    public static LinePricing Calculate(
        Money unitPrice,
        decimal quantity,
        TaxRate taxRate,
        bool pricesIncludeTax,
        IRoundingPolicy rounding)
    {
        ArgumentNullException.ThrowIfNull(rounding);

        var chargedTotal = rounding.Round(unitPrice * quantity);

        var tax = Money.FromScaled((pricesIncludeTax
            ? taxRate.TaxWithinGross(chargedTotal)
            : taxRate.TaxOnNet(chargedTotal)).ToScaled());

        // Exact scaled subtraction, not a second decimal computation: in the inclusive case this
        // reconstructs chargedTotal - tax bit for bit; in the exclusive case chargedTotal already
        // is the net total and tax is simply added back on top of it by the bill total.
        var lineTotal = pricesIncludeTax
            ? Money.FromScaled(chargedTotal.ToScaled() - tax.ToScaled())
            : chargedTotal;

        return new LinePricing(lineTotal, tax, chargedTotal);
    }
}

/// <summary>One priced line's tax figures (SRS FR-10.3, task P1-T08 step 4).</summary>
/// <param name="LineTotal">
/// The net line total - what <c>sale_line.line_total</c> stores, and what sums to
/// <c>sale.subtotal</c> (CLAUDE.md invariant 2).
/// </param>
/// <param name="Tax">What <c>sale_line.tax</c> stores - what a bill's tax sums from, never recomputed from the bill total.</param>
/// <param name="ChargedTotal">
/// What the customer actually pays for this line before any discount - <see cref="LineTotal"/>
/// plus <see cref="Tax"/>. Informational: nothing stores this figure on its own, it is always
/// <see cref="LineTotal"/> and <see cref="Tax"/> that are persisted.
/// </param>
public sealed record LinePricing(Money LineTotal, Money Tax, Money ChargedTotal);
