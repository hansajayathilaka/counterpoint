using System;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Cash;

/// <summary>The cash movement that was just recorded (task P3-T01 "Do this" #1).</summary>
/// <param name="MovementId"><c>cash_movement.id</c>.</param>
/// <param name="Direction">Which way the money moved.</param>
/// <param name="Amount">The positive magnitude moved.</param>
/// <param name="OccurredAt">When it was recorded.</param>
/// <param name="PrintJobId">
/// The outbox row queued for the slip, or null when <c>PrintSlip</c> was not requested.
/// </param>
public sealed record RecordedCashMovement(
    long MovementId,
    CashMovementDirection Direction,
    Money Amount,
    DateTimeOffset OccurredAt,
    long? PrintJobId);
