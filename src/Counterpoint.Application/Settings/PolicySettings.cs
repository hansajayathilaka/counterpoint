using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Settings;

/// <summary>
/// FR-10.5 - the trading rules every later feature reads its limits from: returns, refunds,
/// discount ceilings and what happens when stock runs out.
/// </summary>
/// <param name="ReturnWindowDays">
/// How many days after a sale a return is accepted (FR-5, P2-T01 enforces it).
/// </param>
/// <param name="AllowUnlinkedReturns">
/// Whether a return may be taken without the bill it came from. False: the shop's answer to Q-03
/// is that a return must reference a previous bill number. An owner override is the way round it
/// (P2-T03), not this switch.
/// </param>
/// <param name="DefaultRefundMethod">What a refund is paid out as unless the cashier changes it.</param>
/// <param name="CashRefundLimit">
/// The most that may be refunded in cash on one return. <see cref="Money.Zero"/> means no limit -
/// a shop that wants to refuse cash refunds sets <paramref name="DefaultRefundMethod"/> instead.
/// </param>
/// <param name="MaxLineDiscountRate">
/// The cashier's ceiling on a single line's discount, before <c>product.max_discount_rate</c> is
/// also applied. 100% means no restriction, which is the shop's answer to Q-12 ("not for now").
/// P1-T08 enforces it and requires an owner override above it.
/// </param>
/// <param name="MaxBillDiscountRate">
/// The same ceiling for a discount taken on the whole bill. 100% means no restriction.
/// </param>
/// <param name="NegativeStock">What a sale does when the balance would go below zero (Q-11).</param>
/// <param name="RestockingFeeRate">
/// Proportion of the refund the shop keeps on a return. Zero by default.
/// </param>
public sealed record PolicySettings(
    int ReturnWindowDays,
    bool AllowUnlinkedReturns,
    RefundMethod DefaultRefundMethod,
    Money CashRefundLimit,
    Percentage MaxLineDiscountRate,
    Percentage MaxBillDiscountRate,
    NegativeStockPolicy NegativeStock,
    Percentage RestockingFeeRate);
