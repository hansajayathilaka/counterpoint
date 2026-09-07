using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Security;

/// <summary>
/// The one road every owner override takes (SRS FR-1.7, FR-1.6, engineering guide §4.7).
/// </summary>
/// <remarks>
/// <para>
/// Unlinked refund, over-limit discount, non-returnable override, no-sale drawer, price below
/// cost, restore - all of them come through here, so there is one definition of what an override
/// is, one audit shape, and one place to change if the rule changes. The commands that spend the
/// token are later tasks; this is the mechanism they will use, not a feature in its own right.
/// </para>
/// <para>
/// The cashier is not signed out and the bill on the screen is not touched. That is FR-1.7 in
/// full: <see cref="IAuthenticationService.ReauthenticateAsync"/> verifies the owner's password
/// without disturbing <see cref="ISession"/>.
/// </para>
/// </remarks>
public interface IOwnerOverrideService
{
    /// <summary>
    /// Re-authenticates an owner, writes the audit row carrying both user ids, and returns a
    /// single-use token for the calling command to consume.
    /// </summary>
    /// <exception cref="NotAuthorisedException">
    /// Nobody is signed in, the credential did not verify, or the account given is not an owner.
    /// It throws rather than returning an unusable token: an override that was refused must not
    /// be something a caller can carry on holding.
    /// </exception>
    public Task<OverrideToken> RequestAsync(
        OwnerOverrideRequest request,
        CancellationToken cancellationToken = default);
}
