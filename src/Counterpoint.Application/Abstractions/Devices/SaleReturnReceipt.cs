using System;
using System.Collections.Generic;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>
/// A completed return as the receipt needs it (SRS FR-5, task P2-T02 step 4). It carries no cost
/// and no margin (CLAUDE.md invariant 8), the same as <see cref="SaleReceipt"/>.
/// </summary>
/// <param name="ReturnNo">The allocated return number, printed and encoded in the barcode.</param>
/// <param name="OriginalBillNo">The bill this return was taken against.</param>
/// <param name="ReturnedAt">When the return was taken.</param>
/// <param name="Lines">The return lines, in order.</param>
/// <param name="Subtotal">Sum of the line refunds.</param>
/// <param name="Tax">Tax refunded.</param>
/// <param name="RestockingFee">The fee kept, shown separately (task P2-T02 step 5).</param>
/// <param name="TotalRefund">What is actually paid back.</param>
/// <param name="RefundMethod">How it was paid back.</param>
/// <param name="CashierName">The seller's display name.</param>
/// <param name="PolicyText">
/// The shop's return policy, in the same words settings and enforcement both read from
/// (<c>ReturnPolicyTextFormatter</c>, SRS NFR-L3) - printed so a customer disputing a second
/// return is shown the same rule the till applied.
/// </param>
public sealed record SaleReturnReceipt(
    string ReturnNo,
    string OriginalBillNo,
    DateTimeOffset ReturnedAt,
    IReadOnlyList<SaleReturnReceiptLine> Lines,
    Money Subtotal,
    Money Tax,
    Money RestockingFee,
    Money TotalRefund,
    string RefundMethod,
    string CashierName,
    string PolicyText);
