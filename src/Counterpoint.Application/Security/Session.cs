using System;
using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// The one session on the one machine (C-01).
/// </summary>
/// <remarks>
/// <para>
/// The mutators are <c>internal</c>: only <see cref="AuthenticationService"/>, in this assembly,
/// can change who is signed in, and it does so only after a password has verified and an audit
/// row has been written. Everything else - every viewmodel, every other service - is handed
/// <see cref="ISession"/> and can read the answer but not write it.
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
}
