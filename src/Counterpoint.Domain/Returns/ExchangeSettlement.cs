using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Returns;

/// <summary>
/// Nets a linked return's value against a replacement sale's pre-discount, pre-rounding value
/// into one settlement (SRS FR-5 exchange, AC-04, task P2-T04).
/// </summary>
/// <remarks>
/// <para>
/// <b>The one rule.</b> As much of the return's value as the replacement can absorb is applied as
/// a credit against it, never paid out and never collected twice. What is left over falls one of
/// two ways: if the replacement costs more, the shortfall is <see cref="ExchangeSettlementResult.AmountOwed"/>,
/// collected exactly as an ordinary sale collects tender (<c>Domain.Sales.TenderCalculator</c>,
/// over the caller's own bill-rounding step - this method stops one step short of that, at the
/// pre-rounding total, so the caller can round it exactly the way
/// <c>Counterpoint.Application.Sales.CompleteSaleHandler.PriceAsync</c> rounds every other bill).
/// If the return is worth more than the replacement, the surplus is
/// <see cref="ExchangeSettlementResult.LeftoverRefund"/>, paid back for real - cash, card, or a
/// credit note once P2-T05 exists to issue one.
/// </para>
/// <para>
/// Pure and framework-free, exactly like <see cref="Sales.TenderCalculator"/>: <see cref="Money"/>
/// arithmetic only, no I/O, provable with a hand-worked example alone.
/// </para>
/// </remarks>
public static class ExchangeSettlement
{
    /// <summary>
    /// Settles one exchange.
    /// </summary>
    /// <param name="returnValue">
    /// What the returned goods are worth - <c>sale_return.total_refund</c> (subtotal + tax, less
    /// any restocking fee). Never negative.
    /// </param>
    /// <param name="replacementPreDiscountTotal">
    /// What the replacement goods are worth before any credit is applied - the new sale's
    /// subtotal plus tax, before the bill-total rounding point (CLAUDE.md invariant 2). Never
    /// negative.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is negative.</exception>
    public static ExchangeSettlementResult Calculate(Money returnValue, Money replacementPreDiscountTotal)
    {
        if (returnValue.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(returnValue), returnValue.Amount, "A return's value cannot be negative.");
        }

        if (replacementPreDiscountTotal.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacementPreDiscountTotal),
                replacementPreDiscountTotal.Amount,
                "A replacement's value cannot be negative.");
        }

        // As much of the return as the replacement can take, never more than either side is
        // actually worth - the two ways this can end (a shortfall to collect, or a surplus to
        // refund) are mutually exclusive by construction: one of AmountOwed/LeftoverRefund is
        // always zero.
        var creditApplied = returnValue <= replacementPreDiscountTotal ? returnValue : replacementPreDiscountTotal;
        var amountOwed = replacementPreDiscountTotal - creditApplied;
        var leftoverRefund = returnValue - creditApplied;

        return new ExchangeSettlementResult(creditApplied, amountOwed, leftoverRefund);
    }
}

/// <summary>One exchange, settled (task P2-T04).</summary>
/// <param name="CreditApplied">
/// How much of the return's value was consumed by the replacement - what the new sale's
/// <c>bill_discount</c> carries, not a promotional discount but a credit funded by goods handed
/// back rather than cash (a documented, interim use of that column - see
/// <c>Counterpoint.Application.Exchanges.CreateExchangeHandler</c>'s own remarks).
/// </param>
/// <param name="AmountOwed">
/// What the replacement still costs after the credit, before the bill-total rounding point.
/// Positive only when the replacement is worth more than what came back (AC-04's "higher-priced
/// replacement"); zero otherwise.
/// </param>
/// <param name="LeftoverRefund">
/// What the return was worth beyond what the replacement could absorb - paid back for real.
/// Positive only when the replacement is worth less than what came back; zero otherwise.
/// </param>
public sealed record ExchangeSettlementResult(Money CreditApplied, Money AmountOwed, Money LeftoverRefund);
