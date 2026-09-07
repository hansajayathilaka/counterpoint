using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// Who is signed in, what they may do, and which shift they are trading in (SRS FR-1.1, C-01).
/// </summary>
/// <remarks>
/// <para>
/// One machine, one till, one active session (C-01), so this is a singleton and not a per-request
/// scope. There is no ambient user to look up and no thread-local identity to lose: there is one
/// person at the counter.
/// </para>
/// <para>
/// Read-only on purpose. Signing in and out happens through
/// <see cref="IAuthenticationService"/>, which verifies a password and writes an audit row before
/// the state here changes. Nothing that merely holds an <see cref="ISession"/> can promote
/// itself.
/// </para>
/// </remarks>
public interface ISession
{
    /// <summary>The signed-in user, or null before anyone has signed in.</summary>
    public AuthenticatedUser? CurrentUser { get; }

    /// <summary>True once somebody has signed in.</summary>
    public bool IsAuthenticated { get; }

    /// <summary>The signed-in user's role, or null when nobody is signed in.</summary>
    public Role? Role { get; }

    /// <summary>
    /// The open shift this session is trading in, or null when the till has no shift open.
    /// </summary>
    /// <remarks>
    /// Read from the one open shift at sign-in (C-01 permits exactly one). Opening and closing a
    /// shift is P1-T14 and P3-T01; this only reflects what is already open.
    /// </remarks>
    public long? ShiftId { get; }
}
