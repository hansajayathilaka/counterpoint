using System;
using System.Collections.Generic;
using System.Globalization;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Domain.Sales;

/// <summary>
/// Splits a bill's total across one or more tenders (SRS FR-3.16-FR-3.22, FR-3.24-FR-3.26,
/// P1-T10).
/// </summary>
/// <remarks>
/// <para>
/// <b>The one rule.</b> Every tender is applied in the order it was offered, capped at what the
/// bill still owes at that moment. Cash is the only tender type allowed to be offered for more
/// than that: the excess becomes <see cref="TenderPlan.Change"/> rather than a refusal. Every
/// other tender type that is offered for more than what is still owed is refused outright - "put
/// $50 on a card against a $30 balance" is not a sale a card network can partially decline, and a
/// shop has no way to hand a customer $20 back onto a card at the counter.
/// </para>
/// <para>
/// Pure and framework-free: <see cref="Money"/> arithmetic only, no I/O, so it is provable with a
/// hand-worked example alone, exactly like <c>Counterpoint.Domain.Inventory.StockLedgerMath</c>.
/// It is deliberately callable from both <c>Counterpoint.Application.Sales.CompleteSaleHandler</c>
/// (the authoritative check, immediately before a bill is written) and the sales screen's payment
/// panel (a live preview of the change due as the cashier types) - the same function, so the
/// number the cashier is shown before pressing Pay is the number that is actually charged.
/// </para>
/// </remarks>
public static class TenderCalculator
{
    /// <summary>
    /// The one <c>payment.tender_type</c> value this calculator treats specially: the only one
    /// that can be offered for more than what is owed, because it is the only one a shop can hand
    /// change back in.
    /// </summary>
    public const string Cash = "CASH";

    /// <summary>
    /// Splits <paramref name="total"/> across <paramref name="tenders"/>, in the order offered.
    /// </summary>
    /// <param name="total">The bill total. Never negative in this system.</param>
    /// <param name="tenders">
    /// What the cashier is offering. At least one, each a positive amount.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// No tender was offered, one was offered for zero or less, a non-cash tender was offered for
    /// more than the bill still owed at that point, or the tenders together do not reach the
    /// total. It refuses; it never corrects (engineering guide §4.1).
    /// </exception>
    public static TenderPlan Calculate(Money total, IReadOnlyList<TenderLine> tenders)
    {
        if (tenders is null || tenders.Count == 0)
        {
            throw new InvalidOperationException("A completed bill must be tendered.");
        }

        var remaining = total;
        var change = Money.Zero;
        var applied = new List<AppliedTender>(tenders.Count);

        foreach (var tender in tenders)
        {
            if (!tender.Tendered.IsPositive)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"A {tender.TenderType} tender must be more than zero."));
            }

            var isCash = string.Equals(tender.TenderType, Cash, StringComparison.Ordinal);

            if (!isCash && tender.Tendered > remaining)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{tender.TenderType} cannot take more than the {remaining} still owed on this bill. Change is cash only."));
            }

            var appliedAmount = isCash && tender.Tendered > remaining ? remaining : tender.Tendered;

            change += tender.Tendered - appliedAmount;
            remaining -= appliedAmount;
            applied.Add(new AppliedTender(tender.TenderType, appliedAmount, tender.Reference));
        }

        if (!remaining.IsZero)
        {
            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"The tenders come to {total - remaining} but the bill total is {total}. They must match exactly before the bill can be completed."));
        }

        return new TenderPlan(applied, change);
    }
}
