using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>One <c>shift</c> row's header fields (task P3-T01, and the shape a future X/Z report header reuses).</summary>
/// <param name="ShiftId">The row id.</param>
/// <param name="ShiftNo">Its allocated number, for example <c>SH-000002</c>.</param>
/// <param name="UserId">Who opened it.</param>
/// <param name="CashierDisplayName">
/// The name of the user who opened it (<c>app_user.display_name</c>) - the X report (P3-T02) and
/// the Z report (P3-T03) both print who was trading, not just their id.
/// </param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="BusinessDate">The trading day it belongs to.</param>
/// <param name="OpeningFloat">The cash counted in before trading started.</param>
/// <param name="Status"><c>OPEN</c> or <c>CLOSED</c>.</param>
public sealed record ShiftSummary(
    long ShiftId,
    string ShiftNo,
    long UserId,
    string CashierDisplayName,
    DateTimeOffset OpenedAt,
    DateOnly BusinessDate,
    Money OpeningFloat,
    string Status);
