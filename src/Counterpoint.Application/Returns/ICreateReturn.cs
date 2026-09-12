using System.Threading;
using System.Threading.Tasks;

namespace Counterpoint.Application.Returns;

/// <summary>
/// The one way a linked return is taken (SRS FR-5, AC-03, AC-06, task P2-T02).
/// </summary>
/// <remarks>
/// Not owner-only in its own right - the same reasoning as
/// <see cref="IReturnPolicyAuthorisationService"/>: a cashier takes an ordinary, in-window,
/// receipted return without needing anyone's role, and every exception to that is gated by an
/// <see cref="Counterpoint.Application.Security.OverrideToken"/>, not
/// <see cref="Counterpoint.Application.Security.RequiresRoleAttribute"/>.
/// </remarks>
public interface ICreateReturn
{
    /// <summary>
    /// Takes the return as one transaction, or changes nothing at all - the same all-or-nothing
    /// discipline <c>ICompleteSale.CompleteAsync</c> keeps (SRS FR-3.30's return-side cousin).
    /// </summary>
    /// <exception cref="Counterpoint.Application.Returns.ReturnNotEligibleException">
    /// A policy rule refused the return and no override authorises it - AC-06's cumulative-quantity
    /// rule most of all, which never accepts one (task P2-T01 risk note).
    /// </exception>
    public Task<CreatedReturn> CreateAsync(CreateReturnCommand command, CancellationToken cancellationToken = default);
}
