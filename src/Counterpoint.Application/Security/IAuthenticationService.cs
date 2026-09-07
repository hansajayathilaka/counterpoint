using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Security;

/// <summary>
/// Signing in, signing out, and proving who you are again mid-session
/// (SRS FR-1.1, FR-1.7, NFR-S1, NFR-S9).
/// </summary>
public interface IAuthenticationService
{
    /// <summary>
    /// Verifies a password and, on success, starts the one session (C-01).
    /// </summary>
    /// <remarks>
    /// Every attempt is written to <c>audit_log</c>, including the ones that fail and the ones
    /// refused because the account is locked (SRS NFR-S9). It never throws for a wrong password:
    /// the outcome is the return value.
    /// </remarks>
    public Task<LoginResult> LogInAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a password <b>without</b> touching the session, for an owner override
    /// (SRS FR-1.7).
    /// </summary>
    /// <remarks>
    /// The cashier stays signed in and the bill on the screen is untouched - that is the whole
    /// requirement. The same lockout and the same audit trail apply, so an owner's password
    /// cannot be brute-forced through the override prompt either.
    /// </remarks>
    public Task<LoginResult> ReauthenticateAsync(string username, string password, CancellationToken cancellationToken = default);

    /// <summary>Ends the session and records it.</summary>
    public Task LogOutAsync(CancellationToken cancellationToken = default);
}
