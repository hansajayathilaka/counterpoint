using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Writes the one permitted update to <c>shift</c> - closing it (task P3-T03, SRS FR-8.4, FR-8.5,
/// CLAUDE.md invariant 5). <see cref="IShiftWriter"/> only ever inserts; this is the sibling that
/// performs the single update the schema allows.
/// </summary>
/// <remarks>
/// The database's own <c>trg_shift_closed_is_final</c> trigger refuses a second close regardless
/// of what this port is asked to do (SRS FR-8.8) - <see cref="CloseAsync"/> checks the shift is
/// still <c>OPEN</c> first only so a re-close attempt gets a plain-language refusal (SRS UI-06)
/// instead of a raw trigger exception, the same "enforced twice, deliberately" shape
/// <c>OpenShiftHandler</c>'s own remarks describe for <c>ux_one_open_shift</c>.
/// </remarks>
public interface IShiftCloseWriter
{
    /// <summary>
    /// Closes a shift, in the caller's transaction. Refused if the shift is not currently
    /// <c>OPEN</c>.
    /// </summary>
    public Task CloseAsync(ShiftClose close, CancellationToken cancellationToken = default);
}
