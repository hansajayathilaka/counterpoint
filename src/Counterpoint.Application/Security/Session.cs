using System;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// The one session on the one machine (C-01).
/// </summary>
/// <remarks>
/// <para>
/// The mutators are <c>internal</c>: only <see cref="AuthenticationService"/> and
/// <see cref="Counterpoint.Application.Shifts.OpenShiftHandler"/>, both in this assembly, can
/// change who is signed in or which shift they are trading in, and each does so only after its
/// own write has committed - a verified sign-in, or a shift actually opened. Everything else -
/// every viewmodel, every other service - is handed <see cref="ISession"/> and can read the
/// answer but not write it.
/// </para>
/// <para>
/// Guarded by a lock even though there is one user: the sign-in happens on the UI thread while a
/// background worker may be reading the current user for an audit stamp, and a torn read of a
/// reference plus a role is not worth the saved nanosecond.
/// </para>
/// </remarks>
public sealed class Session : ISession
{
    private readonly object _gate = new();

    private AuthenticatedUser? _currentUser;
    private long? _shiftId;

    /// <inheritdoc />
    public AuthenticatedUser? CurrentUser
    {
        get
        {
            lock (_gate)
            {
                return _currentUser;
            }
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated => CurrentUser is not null;

    /// <inheritdoc />
    public Role? Role => CurrentUser?.Role;

    /// <inheritdoc />
    public long? ShiftId
    {
        get
        {
            lock (_gate)
            {
                return _shiftId;
            }
        }
    }

    /// <summary>Records a verified sign-in. Called only by <see cref="AuthenticationService"/>.</summary>
    internal void SignIn(AuthenticatedUser user, long? shiftId)
    {
        ArgumentNullException.ThrowIfNull(user);

        lock (_gate)
        {
            _currentUser = user;
            _shiftId = shiftId;
        }
    }

    /// <summary>Ends the session. Called only by <see cref="AuthenticationService"/>.</summary>
    internal void SignOut()
    {
        lock (_gate)
        {
            _currentUser = null;
            _shiftId = null;
        }
    }

    /// <summary>
    /// Records the shift a session has just opened. Called only by
    /// <see cref="Counterpoint.Application.Shifts.OpenShiftHandler"/>, after the shift row has
    /// committed - so a cashier who signs in with no shift open, then opens one, sees it without
    /// a fresh sign-in/sign-out cycle (P1-T14).
    /// </summary>
    internal void SetShiftId(long shiftId)
    {
        lock (_gate)
        {
            _shiftId = shiftId;
        }
    }
}
