using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Returns;

/// <summary>
/// The one way a return with no original bill is taken (SRS FR-5.19, task P2-T03).
/// </summary>
/// <remarks>
/// Not decorated <see cref="Counterpoint.Application.Security.RequiresRoleAttribute"/> - a cashier
/// still takes the return and stays signed in throughout (SRS FR-1.7), the same "the cashier asks,
/// the owner allows" shape <see cref="ICreateReturn"/> and
/// <see cref="IReturnPolicyAuthorisationService"/> use. What makes this path high-friction is that
/// <see cref="CreateUnlinkedReturnCommand.Override"/> is mandatory on every call, not that the
/// method itself needs anyone's role.
/// </remarks>
public interface ICreateUnlinkedReturn
{
    /// <summary>
    /// Takes the return as one transaction, or changes nothing at all - the same all-or-nothing
    /// discipline <see cref="ICreateReturn.CreateAsync"/> keeps.
    /// </summary>
    /// <exception cref="ReturnNotEligibleException">
    /// Unlinked returns are disabled in settings (<c>policy.allow_unlinked_returns = false</c>) -
    /// never overridable, change the setting instead - or <see cref="CreateUnlinkedReturnCommand.Override"/>
    /// does not authorise this attempt (wrong action, expired, or already spent).
    /// </exception>
    public Task<CreatedReturn> CreateAsync(
        CreateUnlinkedReturnCommand command, CancellationToken cancellationToken = default);
}
