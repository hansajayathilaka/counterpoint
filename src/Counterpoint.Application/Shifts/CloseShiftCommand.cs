using System;
using Counterpoint.Domain.ValueObjects;

namespace Counterpoint.Application.Shifts;

/// <summary>Closes a shift with a Z report (SRS FR-8.4, task P3-T03).</summary>
/// <param name="ShiftId">The shift to close. Must be the one currently open on this till.</param>
/// <param name="UserId">Who is closing it - must be the signed-in user (SRS FR-1.1).</param>
/// <param name="CountedCash">
/// The physical cash count the cashier entered (SRS FR-8.4: "prompt the cashier to count and enter
/// physical cash"). Denomination breakdown is optional per the task and not carried here - only
/// the counted total the variance is computed against.
/// </param>
/// <param name="ClosedAt">When the close happened. Never before <c>shift.opened_at</c>.</param>
/// <param name="Note">
/// Explains the variance. Optional unless the variance's magnitude exceeds
/// <c>policy.shift_close_variance_note_threshold</c>, in which case
/// <see cref="ShiftCloseVarianceNoteRequiredException"/> is thrown without one.
/// </param>
public sealed record CloseShiftCommand(
    long ShiftId,
    long UserId,
    Money CountedCash,
    DateTimeOffset ClosedAt,
    string? Note = null);
