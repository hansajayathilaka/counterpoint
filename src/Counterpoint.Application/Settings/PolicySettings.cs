using System.Collections.Generic;
using System.Linq;
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
/// <param name="CombineRepeatScans">
/// Whether scanning the same code twice in a row increments that line's quantity, rather than
/// adding a second line for it (SRS FR-3.2 - "configurable"). True by default.
/// </param>
/// <param name="ReceiptRequired">
/// Whether a return must be identified by its own bill number - scanned or typed - rather than
/// only found by a date/customer/amount search (SRS FR-5.1, Q-03: "should have to previous bill
/// no"). True by default. P2-T01's <c>ReturnPolicy.EvaluateReceiptRequirement</c> enforces it and
/// requires an owner override when it is not met.
/// </param>
/// <param name="NonReturnableCategoryIds">
/// <c>category.id</c> values the shop has declared wholly non-returnable (SRS FR-5.10), on top of
/// the per-product <c>product.non_returnable</c> flag P1-T05 already carries. Empty by default -
/// nothing is non-returnable by category until the owner says so.
/// </param>
/// <param name="AllowedUnlinkedRefundMethods">
/// Which <see cref="RefundMethod"/> values an unlinked return (SRS FR-5.19, task P2-T03 step 4)
/// may be refunded by - deliberately its own, narrower list rather than reusing
/// <see cref="DefaultRefundMethod"/>, because the elevated-risk, no-original-bill path is meant to
/// be restricted harder than an ordinary linked return, not just default differently. Cash is
/// excluded by default - "restricted per settings, default to credit note rather than cash" -
/// while card stays available so the flow is not dead on arrival before <c>credit_note</c> rows
/// exist (P2-T05): a shop that has not yet reached that task can still take an unlinked return by
/// card, but never by cash, without editing this setting.
/// </param>
public sealed record PolicySettings(
    int ReturnWindowDays,
    bool AllowUnlinkedReturns,
    RefundMethod DefaultRefundMethod,
    Money CashRefundLimit,
    Percentage MaxLineDiscountRate,
    Percentage MaxBillDiscountRate,
    NegativeStockPolicy NegativeStock,
    Percentage RestockingFeeRate,
    bool CombineRepeatScans,
    bool ReceiptRequired,
    IReadOnlyList<long> NonReturnableCategoryIds,
    IReadOnlyList<RefundMethod> AllowedUnlinkedRefundMethods)
{
    /// <summary>
    /// Value equality for every field, <see cref="NonReturnableCategoryIds"/> included.
    /// </summary>
    /// <remarks>
    /// A record's compiler-generated equality compares a collection property by reference, which
    /// would make a setting that survives the round trip to <c>app_setting</c> rows and back
    /// compare unequal to the value it started as - exactly the kind of drift NFR-L3 exists to
    /// rule out. This override, plus the matching <see cref="GetHashCode"/>, is the fix: two
    /// <see cref="PolicySettings"/> are equal when their category lists contain the same ids in
    /// the same order. <see cref="SettingsSerializer"/> always stores and reads the list sorted,
    /// so "same order" only matters to a caller that builds one by hand out of order.
    /// </remarks>
    public bool Equals(PolicySettings? other) =>
        other is not null
        && ReturnWindowDays == other.ReturnWindowDays
        && AllowUnlinkedReturns == other.AllowUnlinkedReturns
        && DefaultRefundMethod == other.DefaultRefundMethod
        && CashRefundLimit == other.CashRefundLimit
        && MaxLineDiscountRate == other.MaxLineDiscountRate
        && MaxBillDiscountRate == other.MaxBillDiscountRate
        && NegativeStock == other.NegativeStock
        && RestockingFeeRate == other.RestockingFeeRate
        && CombineRepeatScans == other.CombineRepeatScans
        && ReceiptRequired == other.ReceiptRequired
        && NonReturnableCategoryIds.SequenceEqual(other.NonReturnableCategoryIds)
        && AllowedUnlinkedRefundMethods.SequenceEqual(other.AllowedUnlinkedRefundMethods);

    /// <inheritdoc cref="Equals(PolicySettings?)" />
    public override int GetHashCode()
    {
        var hash = new System.HashCode();
        hash.Add(ReturnWindowDays);
        hash.Add(AllowUnlinkedReturns);
        hash.Add(DefaultRefundMethod);
        hash.Add(CashRefundLimit);
        hash.Add(MaxLineDiscountRate);
        hash.Add(MaxBillDiscountRate);
        hash.Add(NegativeStock);
        hash.Add(RestockingFeeRate);
        hash.Add(CombineRepeatScans);
        hash.Add(ReceiptRequired);

        foreach (var id in NonReturnableCategoryIds)
        {
            hash.Add(id);
        }

        foreach (var method in AllowedUnlinkedRefundMethods)
        {
            hash.Add(method);
        }

        return hash.ToHashCode();
    }
}
