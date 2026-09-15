using System;
using Counterpoint.Application.Security;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Cash;

/// <summary>
/// Cash taken out of the drawer - a petty expense, a supplier payment or banking (SRS FR-8.2, task
/// P3-T01 "Do this" #1).
/// </summary>
/// <param name="ShiftId">The open shift the drawer belongs to.</param>
/// <param name="UserId">The cashier recording it - must be the signed-in user (SRS FR-1.1).</param>
/// <param name="Amount">A positive amount.</param>
/// <param name="Reason">Why - mandatory, typically chosen from <c>policy.cash_out_reasons</c>.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="PrintSlip">Whether to queue a printed slip for this movement (task P3-T01: "an optional printed slip").</param>
/// <param name="OwnerOverride">
/// A token from <see cref="IOwnerOverrideService.RequestAsync"/> for
/// <see cref="CashMovementAuditActions.CashOutAboveThreshold"/>, or null when none has been
/// obtained. Only spent when <see cref="Amount"/> is actually above
/// <c>policy.cash_out_authorisation_threshold</c> (task P3-T01 "Do this" #3).
/// </param>
public sealed record RecordCashOutCommand(
    long ShiftId,
    long UserId,
    Money Amount,
    string Reason,
    DateTimeOffset OccurredAt,
    bool PrintSlip = false,
    OverrideToken? OwnerOverride = null);
