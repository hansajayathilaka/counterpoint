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
/// collected exactly as an ordinary sale collects tender (<c>Domain.Sales.TenderCalculator</c>).
/// If the return is worth more than the replacement, the surplus is
/// <see cref="ExchangeSettlementResult.LeftoverRefund"/>, paid back for real - cash, card, or a
/// credit note (P2-T05).
/// </para>
/// <para>
/// <b>The replacement's value is already rounded when it arrives here</b> - the caller's own
/// bill-total rounding step (CLAUDE.md invariant 2), the same figure that becomes
/// <c>sale.total</c>. That is what guarantees <c>CreditApplied &lt;= replacementTotal</c> exactly,
/// with no rounding-boundary edge case: a credit computed against the pre-rounding figure could
/// end up a fraction of a cent above the rounded total the bill actually settles to, which would
/// make it exceed what <c>Domain.Sales.TenderCalculator</c> - which never lets a non-cash tender
/// exceed what is still owed - would accept as the <c>'EXCHANGE'</c> tender it becomes
/// (<c>Counterpoint.Application.Exchanges.CreateExchangeHandler</c>'s own remarks).
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
    /// <param name="replacementTotal">
    /// What the replacement sale actually comes to - its rounded <c>sale.total</c> (CLAUDE.md
    /// invariant 2), full merchandise value, before any credit settles it. Never negative.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Either argument is negative.</exception>
    public static ExchangeSettlementResult Calculate(Money returnValue, Money replacementTotal)
    {
        if (returnValue.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(returnValue), returnValue.Amount, "A return's value cannot be negative.");
        }

        if (replacementTotal.IsNegative)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replacementTotal),
                replacementTotal.Amount,
                "A replacement's value cannot be negative.");
        }

        // As much of the return as the replacement can take, never more than either side is
        // actually worth - the two ways this can end (a shortfall to collect, or a surplus to
        // refund) are mutually exclusive by construction: one of AmountOwed/LeftoverRefund is
        // always zero.
        var creditApplied = returnValue <= replacementTotal ? returnValue : replacementTotal;
        var amountOwed = replacementTotal - creditApplied;
        var leftoverRefund = returnValue - creditApplied;

        return new ExchangeSettlementResult(creditApplied, amountOwed, leftoverRefund);
    }
}

/// <summary>One exchange, settled (task P2-T04).</summary>
/// <param name="CreditApplied">
/// How much of the return's value was consumed by the replacement - the amount both documents
/// settle as an <c>'EXCHANGE'</c> <c>payment</c> row (<c>ExchangeTenderType0010</c>): a positive
/// tender on the replacement sale, and its exact negative on the return
/// (<c>Counterpoint.Application.Exchanges.CreateExchangeHandler</c>'s own remarks) - not a
/// promotional discount, a credit funded by goods handed back rather than cash.
/// </param>
/// <param name="AmountOwed">
/// What the replacement still costs after the credit - the amount <c>Domain.Sales.TenderCalculator</c>
/// still has to collect from a real tender. Positive only when the replacement is worth more than
/// what came back (AC-04's "higher-priced replacement"); zero otherwise.
/// </param>
/// <param name="LeftoverRefund">
/// What the return was worth beyond what the replacement could absorb - paid back for real.
/// Positive only when the replacement is worth less than what came back; zero otherwise.
/// </param>
public sealed record ExchangeSettlementResult(Money CreditApplied, Money AmountOwed, Money LeftoverRefund);
