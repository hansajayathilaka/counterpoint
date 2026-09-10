using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Shifts;

/// <summary>The shift that was just opened.</summary>
/// <param name="ShiftId">Its row id - what <c>sale.shift_id</c> and <c>Session.ShiftId</c> carry from here on.</param>
/// <param name="ShiftNo">Its allocated number, for example <c>SH-000002</c>.</param>
/// <param name="OpenedAt">When it opened.</param>
/// <param name="OpeningFloat">The float it opened with.</param>
public sealed record OpenedShift(long ShiftId, string ShiftNo, DateTimeOffset OpenedAt, Money OpeningFloat);
