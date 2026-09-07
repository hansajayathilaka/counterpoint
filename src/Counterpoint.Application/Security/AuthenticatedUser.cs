using Counterpoint.Domain.Security;

namespace Counterpoint.Application.Security;

/// <summary>
/// The signed-in user, as everything above the Application layer is allowed to see them.
/// </summary>
/// <param name="Id">The <c>app_user.id</c> every audit row and every document is stamped with.</param>
/// <param name="Username">What they typed to sign in.</param>
/// <param name="DisplayName">What the screen calls them.</param>
/// <param name="Role">What they may do (SRS §3.3).</param>
/// <remarks>
/// It carries no password hash, no failed-attempt count and no lockout time. Those belong to the
/// authentication decision and have no business travelling to a viewmodel.
/// </remarks>
public sealed record AuthenticatedUser(long Id, string Username, string DisplayName, Role Role);
