using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// A completed exchange as the receipt needs it: what came back, what went out, and the net (SRS
/// FR-5 exchange, task P2-T04 step 4). It carries no cost and no margin (CLAUDE.md invariant 8),
/// the same as <see cref="SaleReceipt"/> and <see cref="SaleReturnReceipt"/>.
/// </summary>
/// <remarks>
/// One document, one <c>print_job</c> row, for both halves of the exchange - never two receipts a
/// customer has to reconcile by hand. <see cref="ReturnValue"/> is what the return side
/// contributed, and <see cref="CreditApplied"/> is how much of it the replacement actually
/// absorbed; the two differ only when <see cref="RefundPaid"/> is positive (the return was worth
/// more than the replacement, and the surplus was paid back for real rather than credited).
/// </remarks>
/// <param name="ReturnNo">The allocated return number - what came back.</param>
/// <param name="OriginalBillNo">The bill the returned goods were originally sold on.</param>
/// <param name="BillNo">The allocated new bill number - what went out.</param>
/// <param name="ExchangedAt">When the exchange was taken.</param>
/// <param name="ReturnedLines">What came back, at the price originally paid (AC-03).</param>
/// <param name="ReturnValue">
/// What the returned goods are worth net of the restocking fee - <c>sale_return.total_refund</c>.
/// </param>
/// <param name="RestockingFee">The fee kept on the return side, shown separately.</param>
/// <param name="ReplacementLines">What went out, at today's price.</param>
/// <param name="ReplacementTotal">
/// What the replacement is worth before the credit is applied - the new sale's subtotal plus tax.
/// </param>
/// <param name="CreditApplied">
/// How much of <see cref="ReturnValue"/> was applied against <see cref="ReplacementTotal"/> - the
/// new sale's <c>bill_discount</c>, printed here under its own name rather than as an
/// undifferentiated "Discount" (see <c>CreateExchangeHandler</c>'s own remarks on that interim
/// choice).
/// </param>
/// <param name="AmountOwed">
/// What the customer still owed after the credit - <c>sale.total</c>. Positive only for a
/// higher-priced replacement (AC-04); zero otherwise.
/// </param>
/// <param name="Tenders">How <see cref="AmountOwed"/> was actually paid. Empty when nothing was owed.</param>
/// <param name="Change">What was handed back on an over-tender of <see cref="AmountOwed"/>.</param>
/// <param name="RefundPaid">
/// What was paid back for real, beyond the credit - only positive for a lower-priced replacement
/// whose returned value exceeded what the replacement could absorb.
/// </param>
/// <param name="RefundMethod">
/// How <see cref="RefundPaid"/> was paid back, or null when nothing was (<see cref="RefundPaid"/> is zero).
/// </param>
/// <param name="CashierName">The seller's display name.</param>
/// <param name="PolicyText">The shop's return policy, the same text every return receipt carries (SRS NFR-L3).</param>
public sealed record ExchangeReceipt(
    string ReturnNo,
    string OriginalBillNo,
    string BillNo,
    DateTimeOffset ExchangedAt,
    IReadOnlyList<SaleReturnReceiptLine> ReturnedLines,
    Money ReturnValue,
    Money RestockingFee,
    IReadOnlyList<SaleReceiptLine> ReplacementLines,
    Money ReplacementTotal,
    Money CreditApplied,
    Money AmountOwed,
    IReadOnlyList<SaleReceiptTender> Tenders,
    Money Change,
    Money RefundPaid,
    string? RefundMethod,
    string CashierName,
    string PolicyText);
