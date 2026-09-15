using System;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Devices;

/// <summary>The printed slip for one cash movement (task P3-T01 "Do this" #1: "an optional printed slip").</summary>
/// <param name="MovementId"><c>cash_movement.id</c>, printed as a reference number.</param>
/// <param name="ShiftNo">The shift the movement belongs to.</param>
/// <param name="Direction">Which way the money moved.</param>
/// <param name="Amount">The positive magnitude moved.</param>
/// <param name="Reason">Why.</param>
/// <param name="OccurredAt">When.</param>
/// <param name="CashierName">Who recorded it.</param>
public sealed record CashMovementSlip(
    long MovementId,
    string ShiftNo,
    CashMovementDirection Direction,
    Money Amount,
    string Reason,
    DateTimeOffset OccurredAt,
    string CashierName);
