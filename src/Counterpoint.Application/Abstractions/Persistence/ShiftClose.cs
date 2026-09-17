using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// The one permitted update to a <c>shift</c> row - every close field, set together, the moment
/// <c>status</c> moves OPEN to CLOSED (SRS FR-8.4, FR-8.5, task P3-T03, CLAUDE.md invariant 5).
/// </summary>
/// <param name="ShiftId">The shift being closed. Must currently be <c>OPEN</c>.</param>
/// <param name="ClosedAt">When the close happened.</param>
/// <param name="CountedCash"><c>shift.counted_cash</c> - the physical count the cashier entered.</param>
/// <param name="ExpectedCash">
/// <c>shift.expected_cash</c> - <see cref="Counterpoint.Domain.Cash.ExpectedCashCalculator.Calculate"/>'s
/// answer at the moment of closing, never recomputed afterwards.
/// </param>
/// <param name="Variance"><c>shift.variance</c> - <see cref="CountedCash"/> minus <see cref="ExpectedCash"/>.</param>
/// <param name="ClosedBy">
/// <c>shift.closed_by</c> - who actually closed it. Not always <see cref="NewShift.UserId"/>: a
/// shift recovered after a restart (SRS FR-8.7) can be closed by whoever is signed in when trading
/// resumes, not only by the cashier who opened it.
/// </param>
/// <param name="Note">
/// <c>shift.note</c> - mandatory once <see cref="Variance"/>'s magnitude exceeds
/// <c>policy.shift_close_variance_note_threshold</c> (SRS FR-8.4, task P3-T03 "Do this" #1).
/// </param>
public sealed record ShiftClose(
    long ShiftId,
    DateTimeOffset ClosedAt,
    Money CountedCash,
    Money ExpectedCash,
    Money Variance,
    long ClosedBy,
    string? Note);
