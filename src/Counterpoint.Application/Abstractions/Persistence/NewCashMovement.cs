using System;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>A cash movement about to be inserted into <c>cash_movement</c> (SRS FR-8.2, task P3-T01).</summary>
/// <param name="ShiftId">The open shift the drawer belongs to.</param>
/// <param name="Direction">Which way the money moved.</param>
/// <param name="Amount">Always a positive magnitude - the sign lives in <paramref name="Direction"/>.</param>
/// <param name="Reason">Why - mandatory, from the configured reason list or typed free text.</param>
/// <param name="UserId">Who recorded it.</param>
/// <param name="OccurredAt">When.</param>
public sealed record NewCashMovement(
    long ShiftId,
    CashMovementDirection Direction,
    Money Amount,
    string Reason,
    long UserId,
    DateTimeOffset OccurredAt);
