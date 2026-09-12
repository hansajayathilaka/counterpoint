using System;
using System.Collections.Generic;
using Counterpoint.Application.Security;
using Counterpoint.Application.Settings;

namespace Counterpoint.Application.Returns;

/// <summary>
/// Takes a return with no original bill: the SRS's own words for this are "the main
/// return-fraud exposure" (§19) - a deliberately high-friction path, disabled by default and
/// never taken without an owner standing over it (SRS FR-5.19, NFR-S2, task P2-T03).
/// </summary>
/// <remarks>
/// <para>
/// <b>Unlike every override on <see cref="CreateReturnCommand"/>, <see cref="Override"/> and
/// <see cref="Reason"/> here are not optional.</b> A linked return only needs an owner
/// standing by for the rule it happens to break - most linked returns break none of them and
/// carry no override tokens at all. An unlinked return always breaks the one rule that matters
/// most (there is no bill to check anything else against), so both fields are typed as mandatory,
/// not merely validated as such at run time: there is no way to construct this command without
/// them, the same "structurally impossible to skip" discipline
/// <see cref="Counterpoint.Domain.Returns.ReturnEligibility.Denied"/> uses for AC-06.
/// </para>
/// <para>
/// There is no <c>BillReferencePresented</c> parameter the way <see cref="CreateReturnCommand"/>
/// has one - by definition there is no bill reference to have presented or not.
/// </para>
/// </remarks>
/// <param name="UserId">The cashier taking the return.</param>
/// <param name="ShiftId">The open shift it belongs to. A closed shift is refused by the database (AC-11).</param>
/// <param name="ReturnedAt">When the return is being taken.</param>
/// <param name="Lines">What is coming back, and at what price. At least one.</param>
/// <param name="RefundMethod">
/// How the refund is paid out - restricted to
/// <see cref="PolicySettings.AllowedUnlinkedRefundMethods"/> (task P2-T03 step 4), and, as
/// <see cref="CreateReturnCommand.RefundMethod"/>, never <see cref="RefundMethod.CreditNote"/>
/// until P2-T05 exists to back one with an actual <c>credit_note</c> row.
/// </param>
/// <param name="Override">
/// A token from <c>IOwnerOverrideService.RequestAsync</c> for
/// <see cref="ReturnPolicyAuditActions.UnlinkedReturn"/>. Mandatory - see the remarks.
/// </param>
/// <param name="Reason">
/// Why this return has no bill, recorded on <c>sale_return.reason</c>. Mandatory - see the
/// remarks; a blank string is refused the same way <c>IOwnerOverrideService.RequestAsync</c>
/// refuses a blank reason for the override itself.
/// </param>
/// <param name="CustomerId">The customer this return is against, if known. Optional.</param>
public sealed record CreateUnlinkedReturnCommand(
    long UserId,
    long ShiftId,
    DateTimeOffset ReturnedAt,
    IReadOnlyList<UnlinkedReturnLineRequest> Lines,
    RefundMethod RefundMethod,
    OverrideToken Override,
    string Reason,
    long? CustomerId = null);
