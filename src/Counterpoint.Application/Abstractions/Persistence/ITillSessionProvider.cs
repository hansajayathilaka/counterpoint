using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Abstractions.Persistence;

/// <summary>
/// Supplies the user and shift a bill is posted against.
/// </summary>
/// <remarks>
/// <b>A placeholder with a short life.</b> The walking skeleton has no sign-in, so the
/// implementation reads the seeded owner and the one open shift straight out of the database.
/// Authentication and the session's role are P1-T02; shift open and close are P3-T01. Both
/// replace this, and the sale path does not change when they do - which is the point of it
/// being a port.
/// </remarks>
public interface ITillSessionProvider
{
    /// <summary>
    /// The current session, or null when there is no open shift to trade in - which the UI
    /// shows as "open a shift first", not as an error.
    /// </summary>
    public Task<TillSession?> GetCurrentAsync(CancellationToken cancellationToken = default);
}
