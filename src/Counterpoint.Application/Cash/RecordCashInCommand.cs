using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Cash;

/// <summary>Cash added to the drawer - a float top-up or an owner deposit (SRS FR-8.2, task P3-T01 "Do this" #1).</summary>
/// <param name="ShiftId">The open shift the drawer belongs to.</param>
/// <param name="UserId">The cashier recording it - must be the signed-in user (SRS FR-1.1).</param>
/// <param name="Amount">A positive amount.</param>
/// <param name="Reason">Why - mandatory, typically chosen from <c>policy.cash_in_reasons</c>.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="PrintSlip">Whether to queue a printed slip for this movement (task P3-T01: "an optional printed slip").</param>
public sealed record RecordCashInCommand(
    long ShiftId,
    long UserId,
    Money Amount,
    string Reason,
    DateTimeOffset OccurredAt,
    bool PrintSlip = false);
