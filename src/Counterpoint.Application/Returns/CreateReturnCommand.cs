using System;
using System.Collections.Generic;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Returns;

/// <summary>
/// Takes a linked return against a completed bill: pick lines and quantities, choose a
/// disposition per line, refund at the price originally paid, in one transaction (SRS
/// FR-5.1-FR-5.10, AC-03, AC-06, task P2-T02).
/// </summary>
/// <param name="OriginalSaleId">
/// The bill this return is against. Always given in this task's flow - an unlinked return
/// (<c>original_sale_id</c> null) is P2-T03.
/// </param>
/// <param name="UserId">The cashier taking the return.</param>
/// <param name="ShiftId">The open shift it belongs to. A closed shift is refused by the database (AC-11).</param>
/// <param name="ReturnedAt">When the return is being taken.</param>
/// <param name="Lines">What is coming back, and how. At least one.</param>
/// <param name="RefundMethod">
/// How the refund is paid out. <see cref="Counterpoint.Application.Settings.RefundMethod.CreditNote"/>
/// issues a numbered <c>credit_note</c> row alongside the refund payment (task P2-T05) - see
/// <see cref="CreditNoteExpiresOn"/> for its optional expiry.
/// </param>
/// <param name="BillReferencePresented">
/// True when the bill was identified by its own number, scanned or typed, rather than only found
/// by <see cref="Counterpoint.Application.Abstractions.Persistence.IReturnableSaleLookup.SearchAsync"/>
/// (SRS FR-5.1, Q-03).
/// </param>
/// <param name="ReturnWindowOverride">
/// A token from <c>IOwnerOverrideService.RequestAsync</c> for
/// <see cref="ReturnPolicyAuditActions.ReturnWindowExceeded"/>, spent only if the sale is outside
/// the configured window (SRS FR-5.6).
/// </param>
/// <param name="NonReturnableOverride">
/// A token for <see cref="ReturnPolicyAuditActions.NonReturnableOverride"/> (SRS FR-5.10, AC-05).
/// One token authorises one non-returnable line - a return with several such lines needs a fresh
/// token for each, because <c>OverrideToken</c> is single-use by design (task P2-T01); this is
/// not a bug.
/// </param>
/// <param name="ReceiptRequirementOverride">
/// A token for <see cref="ReturnPolicyAuditActions.ReceiptNotPresented"/>, spent only when
/// <paramref name="BillReferencePresented"/> is false and the policy requires one (SRS FR-5.1).
/// </param>
/// <param name="CashRefundLimitOverride">
/// A token for <see cref="ReturnPolicyAuditActions.CashRefundLimitExceeded"/>, spent only for a
/// cash refund above the configured limit (SRS FR-5.13).
/// </param>
/// <param name="Reason">
/// The return's own reason, recorded on <c>sale_return.reason</c> alongside each line's own
/// (<see cref="ReturnLineRequest.Reason"/>).
/// </param>
/// <param name="CreditNoteExpiresOn">
/// The last business date a credit note issued by this return may be redeemed on, or null to
/// never expire (task P2-T05). Ignored unless <paramref name="RefundMethod"/> is
/// <see cref="Counterpoint.Application.Settings.RefundMethod.CreditNote"/> - there is no
/// shop-wide default expiry policy in <c>PolicySettings</c> for this to fall back to, so a caller
/// that wants one always states it.
/// </param>
public sealed record CreateReturnCommand(
    long OriginalSaleId,
    long UserId,
    long ShiftId,
    DateTimeOffset ReturnedAt,
    IReadOnlyList<ReturnLineRequest> Lines,
    RefundMethod RefundMethod,
    bool BillReferencePresented = true,
    OverrideToken? ReturnWindowOverride = null,
    OverrideToken? NonReturnableOverride = null,
    OverrideToken? ReceiptRequirementOverride = null,
    OverrideToken? CashRefundLimitOverride = null,
    string? Reason = null,
    DateOnly? CreditNoteExpiresOn = null);
