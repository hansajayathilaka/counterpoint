using System;
using Counterpoint.Domain.Cash;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One row of <c>cash_movement</c>, read back for the shift's own history (SRS FR-8.2, task P3-T01 "Do this" #4).</summary>
/// <param name="Id">The row id.</param>
/// <param name="ShiftId">The shift it was recorded against.</param>
/// <param name="Direction">Which way the money moved.</param>
/// <param name="Amount">The positive magnitude moved.</param>
/// <param name="Reason">Why.</param>
/// <param name="UserId">Who recorded it.</param>
/// <param name="UserDisplayName">Their name, for the history screen.</param>
/// <param name="OccurredAt">When.</param>
public sealed record CashMovementRecord(
    long Id,
    long ShiftId,
    CashMovementDirection Direction,
    Money Amount,
    string Reason,
    long UserId,
    string UserDisplayName,
    DateTimeOffset OccurredAt);
