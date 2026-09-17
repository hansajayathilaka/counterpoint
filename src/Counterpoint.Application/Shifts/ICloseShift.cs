using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Shifts;

/// <summary>
/// Closes a shift with a Z report (SRS FR-8.4, FR-8.5, FR-8.8, task P3-T03, AC-11).
/// </summary>
/// <remarks>
/// No <see cref="Counterpoint.Application.Security.RequiresRoleAttribute"/>: closing a shift is an
/// ordinary cashier capability, the same as <see cref="IOpenShift"/>'s own remarks already say for
/// opening one - not owner-only. There is only ever one shift open at a time (C-01), so "may only
/// close their own shift" collapses to "may only close the one shift currently open on this till",
/// exactly the check <see cref="CloseShiftHandler"/> makes.
/// </remarks>
public interface ICloseShift
{
    /// <summary>
    /// Closes a shift: computes the variance, writes every close field in one transaction together
    /// with the day's rollups, an audit row and the Z report's <c>print_job</c>, then triggers a
    /// backup (task P3-T03 "Do this" #2).
    /// </summary>
    /// <exception cref="Counterpoint.Application.Security.NotAuthorisedException">
    /// Nobody is signed in, or the caller is not the session currently trading in this shift.
    /// </exception>
    /// <exception cref="System.InvalidOperationException">
    /// The shift does not exist, or is already closed (SRS FR-8.8 - a Z report can never be
    /// re-run).
    /// </exception>
    /// <exception cref="ShiftCloseVarianceNoteRequiredException">
    /// The variance's magnitude is above <c>policy.shift_close_variance_note_threshold</c> and
    /// <see cref="CloseShiftCommand.Note"/> was blank.
    /// </exception>
    public Task<ClosedShift> CloseAsync(CloseShiftCommand command, CancellationToken cancellationToken = default);
}
