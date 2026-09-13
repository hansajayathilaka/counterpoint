using System;
using System.Collections.Generic;
using Counterpoint.Application.Returns;
using Counterpoint.Application.Sales;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Exchanges;

/// <summary>
/// Takes an exchange against a completed bill: return one or more lines, replace them with one or
/// more new items, and settle the difference once - in one transaction (SRS FR-5 exchange, AC-04,
/// task P2-T04).
/// </summary>
/// <remarks>
/// <para>
/// Built from a linked return only - the far more common case, and the one <c>ReturnLines</c>'s
/// own type (<see cref="ReturnLineRequest"/>) is already shaped for
/// (<see cref="Counterpoint.Application.Returns.CreateReturnCommand"/>, task P2-T02). An unlinked
/// exchange (no original bill) is not asked for by this task and is not built here; the phase
/// doc's own risk note is about reports double-counting a <em>linked</em> exchange, which is the
/// case this command exists to make impossible.
/// </para>
/// <para>
/// <see cref="DifferenceTenders"/> is read only when the replacement costs more than the return is
/// worth (AC-04's "higher-priced replacement") - <see cref="Domain.Returns.ExchangeSettlement"/>
/// decides which, not the caller. Offering tenders when nothing is owed is refused, the same
/// discipline <c>Domain.Sales.TenderCalculator</c> already applies to an ordinary sale.
/// </para>
/// </remarks>
/// <param name="OriginalSaleId">The bill the returned lines were sold on.</param>
/// <param name="UserId">The cashier taking the exchange.</param>
/// <param name="ShiftId">The open shift it belongs to. A closed shift is refused by the database (AC-11).</param>
/// <param name="ExchangedAt">When the exchange is being taken. Its date is the trading day for both documents.</param>
/// <param name="ReturnLines">What is coming back, and how - at least one.</param>
/// <param name="ReplacementLines">What is going out instead - at least one.</param>
/// <param name="DifferenceTenders">
/// How a higher-priced replacement's difference is paid for - what was actually offered, in the
/// order offered. Ignored, and must be empty, when nothing is owed.
/// </param>
/// <param name="ExcessRefundMethod">
/// How a lower-priced replacement's surplus is paid back, when the return is worth more than the
/// replacement can absorb. Cash or card only - see
/// <see cref="Counterpoint.Application.Returns.RefundMethodMapping.RequireSupported"/> for why
/// <see cref="RefundMethod.CreditNote"/> is not yet offered here (P2-T05) and
/// <see cref="RefundMethod.Exchange"/> can never be, on either side of this same command.
/// </param>
/// <param name="BillReferencePresented">
/// True when the original bill was identified by its own number, scanned or typed (SRS FR-5.1, Q-03).
/// </param>
/// <param name="ReturnWindowOverride">
/// A token for <see cref="ReturnPolicyAuditActions.ReturnWindowExceeded"/>, spent only if the
/// original sale is outside the configured return window (SRS FR-5.6).
/// </param>
/// <param name="NonReturnableOverride">
/// A token for <see cref="ReturnPolicyAuditActions.NonReturnableOverride"/> (SRS FR-5.10, AC-05).
/// </param>
/// <param name="ReceiptRequirementOverride">
/// A token for <see cref="ReturnPolicyAuditActions.ReceiptNotPresented"/>, spent only when
/// <paramref name="BillReferencePresented"/> is false and the policy requires one.
/// </param>
/// <param name="CashRefundLimitOverride">
/// A token for <see cref="ReturnPolicyAuditActions.CashRefundLimitExceeded"/>, spent only when the
/// surplus refund is cash and above the configured limit.
/// </param>
/// <param name="Reason">The exchange's own reason, recorded on <c>sale_return.reason</c>.</param>
public sealed record CreateExchangeCommand(
    long OriginalSaleId,
    long UserId,
    long ShiftId,
    DateTimeOffset ExchangedAt,
    IReadOnlyList<ReturnLineRequest> ReturnLines,
    IReadOnlyList<SaleLineRequest> ReplacementLines,
    IReadOnlyList<TenderRequest> DifferenceTenders,
    RefundMethod ExcessRefundMethod = RefundMethod.Cash,
    bool BillReferencePresented = true,
    OverrideToken? ReturnWindowOverride = null,
    OverrideToken? NonReturnableOverride = null,
    OverrideToken? ReceiptRequirementOverride = null,
    OverrideToken? CashRefundLimitOverride = null,
    string? Reason = null);
