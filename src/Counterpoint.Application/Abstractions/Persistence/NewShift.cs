using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// A shift about to be opened, with its number already allocated.
/// </summary>
/// <param name="ShiftNo">Allocated from <c>number_sequence</c> in the same transaction (CLAUDE.md invariant 4).</param>
/// <param name="UserId">The cashier opening it (SRS FR-8.1).</param>
/// <param name="OpenedAt">When the shift opens.</param>
/// <param name="BusinessDate">The trading day it belongs to.</param>
/// <param name="OpeningFloat">The cash counted into the drawer before trading starts.</param>
public sealed record NewShift(
    string ShiftNo,
    long UserId,
    DateTimeOffset OpenedAt,
    DateOnly BusinessDate,
    Money OpeningFloat);
