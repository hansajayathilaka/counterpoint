using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Supplies the user and shift a bill is posted against.
/// </summary>
/// <remarks>
/// <b>Outlived its own "short life" note.</b> The walking skeleton had no sign-in, and this read
/// the seeded owner and the one open shift straight out of the database as a stand-in for both.
/// Authentication and the session's role are P1-T02's; this remains exactly what makes shift
/// recovery on restart work (SRS FR-8.7) - a fresh <c>Session</c>, at sign-in, asks this for the
/// one open shift, which is a database read and not anything remembered in memory. Opening a
/// shift is P1-T14 (<c>Counterpoint.Application.Shifts.IOpenShift</c>); closing one is P3-T01.
/// </remarks>
public interface ITillSessionProvider
{
    /// <summary>
    /// The current session, or null when there is no open shift to trade in - which the UI
    /// shows as "open a shift first", not as an error.
    /// </summary>
    public Task<TillSession?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
